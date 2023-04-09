using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace sms.Controllers;

[ApiController]
[Route("[controller]")]
public class SmsController : ControllerBase
{
    private Guid _testGuid = Guid.NewGuid();

    private readonly ILogger<SmsController> _logger;
    private readonly IUserBroadcast _userBroadcast;
    private readonly string _connectionString;

    public SmsController(ILogger<SmsController> logger,
        IUserBroadcast userBroadcast,
        IConfiguration configuration)
    {
        _logger = logger;
        _userBroadcast = userBroadcast;
        _connectionString = configuration.GetValue<string>("Database:ConnectionString")!;
    }

    private string? GetUsername() => Request.Headers["X-RapidAPI-User"].FirstOrDefault();

    private static readonly Regex _whitespaceRegex = new Regex(@"\s+");

    [HttpPost("NewMessage")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SimpleOkResponeDto))]
    public async Task<IActionResult> NewMessage([FromBody] NewMessageDto data)
    {
        var tag = new string(data.Message.TakeWhile(x => !char.IsWhiteSpace(x)).ToArray()).Trim().ToUpper();

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var userId = -1;
        using (var command = new MySqlCommand("SELECT id FROM users WHERE tag = @tag",
            connection))
        {
            command.Parameters.AddWithValue("@tag", tag);

            var result = await command.ExecuteScalarAsync();
            if (result != null)
            {
                userId = (int)(uint)result;
            }
        }

        if (userId == -1)
        {
            using (var command = new MySqlCommand("INSERT INTO junk_messages (number_from, number_to, message)" +
                "VALUES (@number_from, @number_to, @message)",
                connection))
            {
                command.Parameters.AddWithValue("@number_from", data.From);
                command.Parameters.AddWithValue("@number_to", data.To);
                command.Parameters.AddWithValue("@message", data.Message);

                await command.ExecuteNonQueryAsync();
            }
        }
        else
        {
            using (var command = new MySqlCommand("INSERT INTO messages " +
                "(user_id, number_from, number_to, message)" +
                "VALUES (@userId, @number_from, @number_to, @message)",
                connection))
            {
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@number_from", data.From);
                command.Parameters.AddWithValue("@number_to", data.To);
                command.Parameters.AddWithValue("@message", data.Message);

                await command.ExecuteNonQueryAsync();
            }

            /* Notify waiting clients */
            _userBroadcast.NewMessage((uint)userId);
        }

        return Ok(new SimpleOkResponeDto());
    }

    [HttpGet("SetTag")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SimpleOkResponeDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(SimpleErrorResponeDto))]
    public async Task<IActionResult> SetTag([FromQuery, Required] string tag)
    {
        tag = tag.Trim().ToUpper();
        if (tag.Length > 16)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "Tag can be no more than 16 characters"
            });
        }
        var username = GetUsername();
        if (username == null)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "No username given"
            });
        }

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            using (var command = new MySqlCommand("SELECT COUNT(*) FROM users " +
                "WHERE tag = @tag and username != @username",
                connection))
            {
                command.Parameters.AddWithValue("@tag", tag);
                command.Parameters.AddWithValue("@username", username);
                if ((long)command.ExecuteScalar() > 0)
                {
                    return BadRequest(new SimpleErrorResponeDto
                    {
                        Message = $"Tag \"{tag}\" is already in use"
                    });
                }
            }

            using (var command = new MySqlCommand("INSERT INTO users (username, tag)" +
                "VALUES (@username, @tag)" +
                "ON DUPLICATE KEY UPDATE tag = @tag, last_access = CURRENT_TIMESTAMP",
                connection))
            {
                command.Parameters.AddWithValue("@tag", tag);
                command.Parameters.AddWithValue("@username", username);

                await command.ExecuteNonQueryAsync();
            }
        }

        return Ok(new SimpleOkResponeDto());
    }

    [HttpGet("GetTag")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GetTagDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(SimpleErrorResponeDto))]
    public async Task<IActionResult> GetTag()
    {
        var username = GetUsername();
        if (username == null)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "No username given"
            });
        }

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        using (var command = new MySqlCommand("SELECT tag FROM users " +
            "WHERE username = @username",
            connection))
        {
            command.Parameters.AddWithValue("@username", username);
            var result = await command.ExecuteScalarAsync();
            if (result != null)
            {
                return Ok(new GetTagDto { Tag = (string)result });
            }
        }

        var tag = UsernameToTag(username);
        using (var command = new MySqlCommand("INSERT INTO users (username, tag)" +
                         "VALUES (@username, @tag)",
                         connection))
        {
            command.Parameters.AddWithValue("@tag", tag);
            command.Parameters.AddWithValue("@username", username);

            await command.ExecuteNonQueryAsync();
        }

        return Ok(new GetTagDto { Tag = tag });
    }

    [HttpGet("GetUpdates")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GetUpdatesDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(SimpleErrorResponeDto))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(SimpleErrorResponeDto))]
    public async Task<IActionResult> GetUpdates(
        [FromQuery(Name = "start_id"), Required] int startId,
        [FromQuery(Name = "time_out")] int timeOut = 300)
    {
        var username = GetUsername();
        if (username == null)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "No username given"
            });
        }

        var messages = new List<int>();

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var userId = await GetOrCreateUserId(connection, username);
        if (userId == -1)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new SimpleErrorResponeDto
            {
                Message = "Something went wrong"
            });
        }

        messages = await GetUpdateIds(connection, userId, startId);
        if (messages.Count > 0 || timeOut == 0)
        {
            return Ok(new GetUpdatesDto
            {
                Messages = messages
            });
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeOut));

        /* Wait for messages */
        try
        {
            _userBroadcast.NewMessageSent += HandleMessage;
            await Task.Delay(-1, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        finally
        {
            _userBroadcast.NewMessageSent -= HandleMessage;
        }

        if (cts.IsCancellationRequested)
        {
            messages = await GetUpdateIds(connection, userId, startId);
        }

        return Ok(new GetUpdatesDto
        {
            Messages = messages
        });

        void HandleMessage(object? sender, UserBroadcastEventArgs args)
        {
            if (args.UserId == userId)
            {
                cts?.Cancel();
            }
        }
    }

    [HttpGet("GetMessage")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GetUpdatesDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(SimpleErrorResponeDto))]
    [ProducesResponseType(StatusCodes.Status500InternalServerError, Type = typeof(SimpleErrorResponeDto))]
    public async Task<IActionResult> GetMessage(
        [FromQuery(Name = "message_id"), Required] int messageId)
    {
        var username = GetUsername();
        if (username == null)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "No username given"
            });
        }

        var messages = new List<int>();

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var userId = await GetOrCreateUserId(connection, username);
        if (userId == -1)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new SimpleErrorResponeDto
                {
                    Message = "Something went wrong"
                });
        }

        string sqlQuery = "SELECT created_at, number_from, number_to, message "
            + "FROM messages WHERE user_id = @user_id AND id = @message_id limit 1";

        using (var command = new MySqlCommand(sqlQuery, connection))
        {
            command.Parameters.AddWithValue("@user_id", userId);
            command.Parameters.AddWithValue("@message_id", messageId);

            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    return Ok(new MessageDTO
                    {
                        CreatedAt = reader.GetDateTime(0),
                        From = reader.GetString(1),
                        To = reader.GetString(2),
                        Message = reader.GetString(3)
                    });
                }
            }
        }

        return NotFound(new SimpleErrorResponeDto
        {
            Message = "Message not found"
        });
    }

    #region Helpers
    private async Task<UInt32?> GetUserId(MySqlConnection connection,
        MySqlTransaction transaction, string username)
    {
        using var command = new MySqlCommand("SELECT id FROM users WHERE username = @username",
            connection, transaction);

        command.Parameters.AddWithValue("@username", username);
        return (UInt32?)await command.ExecuteScalarAsync();
    }

    private string UsernameToTag(string username)
    {
        var tag = _whitespaceRegex.Replace(username, "");
        return tag.Substring(0, Math.Min(16, tag.Length)).ToUpper();
    }

    private async Task<int> GetOrCreateUserId(MySqlConnection connection, string username)
    {
        using var transaction = connection.BeginTransaction();
        try
        {
            using (var command = new MySqlCommand("UPDATE users SET last_access = CURRENT_TIMESTAMP " +
                "WHERE username = @username",
                connection, transaction))
            {
                command.Parameters.AddWithValue("@username", username);
                await command.ExecuteNonQueryAsync();
            }

            var userId = await GetUserId(connection, transaction, username);
            if (userId != null)
            {
                return (int)userId.Value;
            }

            using (var command = new MySqlCommand("INSERT INTO users (username, tag)" +
                "VALUES (@username, @tag)",
                connection))
            {
                command.Parameters.AddWithValue("@tag", UsernameToTag(username));
                command.Parameters.AddWithValue("@username", username);

                await command.ExecuteNonQueryAsync();
            }

            userId = await GetUserId(connection, transaction, username);

            transaction.Commit();

            return (int)userId.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception in GetOrCreateUserId(): {ex}");
            transaction.Rollback();

            return -1;
        }
    }

    private async Task<List<int>> GetUpdateIds(MySqlConnection connection,
        int userId, int startId)
    {
        var messages = new List<int>();

        using var command = new MySqlCommand("SELECT id FROM messages " +
                "WHERE user_id = @user_id AND id >= @start_id ORDER BY id limit 1000",
                connection);
        command.Parameters.AddWithValue("@user_id", userId);
        command.Parameters.AddWithValue("@start_id", startId);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt32(0);
                messages.Add(id);
            }
        }

        return messages;
    }
    #endregion Helpers
}
