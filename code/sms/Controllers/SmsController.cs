using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace sms.Controllers;

[ApiController]
[Route("[controller]")]
public class SmsController : ControllerBase
{
    private readonly ILogger<SmsController> _logger;
    private readonly IBroadcaster _userBroadcast;
    private readonly string _connectionString;
    private readonly string _usernameHeader;

    public SmsController(ILogger<SmsController> logger,
        IBroadcaster userBroadcast,
        IConfiguration configuration)
    {
        _logger = logger;
        _userBroadcast = userBroadcast;
        _connectionString = configuration.GetValue<string>("Database:ConnectionString")!;
        _usernameHeader = configuration.GetValue<string>("Header:Username")!;
    }

    private string? GetUsername() => Request.Headers[_usernameHeader].FirstOrDefault();

    private string GetClientIp() => Request.Headers["X-Forwarded-For"].FirstOrDefault() ??
        "unknown";

    private static readonly Regex _whitespaceRegex = new Regex(@"\s+");
    private Regex _validateNumberRegex = new Regex(@"^\+?\d*$", RegexOptions.Compiled);

    /* End point called by the servier with the SMS daemon, when a net SMS is received on the modem */
    [HttpPost("NewMessage")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SimpleOkResponeDto))]
    public async Task<IActionResult> NewMessage([FromBody] NewMessageDto data)
    {
        _logger.LogInformation($"{GetClientIp()} - " +
            $"NewMessage: from=\"{data.From}\", to=\"{data.To}\"");

        var tag = new string(data.Message.TakeWhile(x => !char.IsWhiteSpace(x)).ToArray()).Trim().ToUpper();

        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        long userId = -1;
        using (var command = new MySqlCommand("SELECT id FROM users WHERE tag = @tag",
            connection))
        {
            command.Parameters.AddWithValue("@tag", tag);

            var result = await command.ExecuteScalarAsync();
            if (result != null)
            {
                userId = (long)result;
            }
        }

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

        if (userId != -1)
        {
            /* Notify waiting clients */
            _userBroadcast.NewMessage(userId);
        }

        return Ok(new SimpleOkResponeDto());
    }

    /* End point called by the servier with the SMS daemon, to check if any message need sending */
    [HttpGet("GetSendMessageUpdates")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SendMessageUpdateResultsDto>))]
    public async Task<IActionResult> GetSendMessageUpdates(
        [FromQuery(Name = "start_id"), Required] long startId,
        [FromQuery(Name = "time_out")] int timeOut = 300)
    {
        _logger.LogInformation($"{GetClientIp()} - " +
            $"GetSendMessageUpdates: start_id={startId}, time_out={timeOut}");
            
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var messages = new List<SendMessageUpdateDto>();

        messages = await GetSendMessageUpdates(connection, startId);
        if (messages.Count > 0 || timeOut == 0)
        {
            return Ok(new SendMessageUpdateResultsDto
            {
                Messages = messages
            });
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeOut));

        /* Wait for messages */
        try
        {
            _userBroadcast.NewSubmitMessage += HandleMessage;
            await Task.Delay(-1, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Ignore
        }
        finally
        {
            _userBroadcast.NewSubmitMessage -= HandleMessage;
        }

        if (cts.IsCancellationRequested)
        {
            messages = await GetSendMessageUpdates(connection, startId);
        }

        return Ok(new SendMessageUpdateResultsDto
        {
            Messages = messages
        });

        void HandleMessage(object? sender, NewSubmitMessageEventArgs args)
        {
            cts?.Cancel();
        }
    }

    [HttpPost("SendMessage")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SimpleOkResponeDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(SimpleErrorResponeDto))]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageDto data)
    {
        _logger.LogInformation($"{GetClientIp()} - " +
            $"SendMessage: from=\"{data.From}\", to=\"{data.To}\"");

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

        var userId = await GetOrCreateUserId(connection, username);

        /* Valiate the phone number */
        if (!_validateNumberRegex.IsMatch(data.To))
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "Invalid to number format"
            });
        }

        if (!_validateNumberRegex.IsMatch(data.From))
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "Invalid from number format"
            });
        }

        using (var command = new MySqlCommand("INSERT INTO send_messages " +
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

        _userBroadcast?.SubmitMessage(data);

        return Ok(new SimpleOkResponeDto());
    }

    [HttpGet("SetTag")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SimpleOkResponeDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(SimpleErrorResponeDto))]
    public async Task<IActionResult> SetTag([FromQuery, Required] string tag)
    {
        var username = GetUsername();

        _logger.LogInformation($"{GetClientIp()} - " +
            $"SetTag: username=\"{(username ?? "<NULL>")}\", tag=\"{tag}\"");

        tag = tag.Trim().ToUpper();
        if (tag.Length > 16)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "Tag can be no more than 16 characters"
            });
        }
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

        _logger.LogInformation($"{GetClientIp()} - " +
            $"GetTag: username=\"{(username ?? "<NULL>")}\"");

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
        [FromQuery(Name = "start_id"), Required] long startId,
        [FromQuery(Name = "time_out")] int timeOut = 0)
    {
        var username = GetUsername();

        _logger.LogInformation($"{GetClientIp()} - " +
            $"GetUpdates: username=\"{(username ?? "<NULL>")}\", " +
            $"start_id={startId}, time_out={timeOut}");

        if (username == null)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "No username given"
            });
        }

        var messages = new List<long>();

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

        void HandleMessage(object? sender, NewMessageSentEventArgs args)
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
        [FromQuery(Name = "message_id"), Required] long messageId)
    {
        var username = GetUsername();

        _logger.LogInformation($"{GetClientIp()} - " +
            $"GetMessage: username=\"{(username ?? "<NULL>")}\", " +
            $"message_id={messageId}");
            
        if (username == null)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "No username given"
            });
        }

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
    private async Task<long?> GetUserId(MySqlConnection connection,
        MySqlTransaction transaction, string username)
    {
        using var command = new MySqlCommand("SELECT id FROM users WHERE username = @username",
            connection, transaction);

        command.Parameters.AddWithValue("@username", username);
        return (long?)await command.ExecuteScalarAsync();
    }

    private string UsernameToTag(string username)
    {
        var tag = _whitespaceRegex.Replace(username, "");
        return tag.Substring(0, Math.Min(16, tag.Length)).ToUpper();
    }

    private async Task<long> GetOrCreateUserId(MySqlConnection connection, string username)
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
                return userId.Value;
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

            return userId.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception in GetOrCreateUserId(): {ex}");
            transaction.Rollback();

            return -1;
        }
    }

    private async Task<List<SendMessageUpdateDto>> GetSendMessageUpdates(
        MySqlConnection connection,
        long startId)
    {
        var messages = new List<SendMessageUpdateDto>();

        using var command = new MySqlCommand(
            "SELECT id, user_id, number_from, number_to, message, created_at FROM send_messages " +
            "WHERE id >= @start_id ORDER BY id limit 1000",
            connection);
        command.Parameters.AddWithValue("@start_id", startId);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var message = new SendMessageUpdateDto
                {
                    Id = reader.GetInt64(0),
                    UserId = reader.GetInt64(1),
                    From = reader.GetString(2),
                    To = reader.GetString(3),
                    Message = reader.GetString(4),
                    CreatedAt = reader.GetDateTime(5)
                };
                messages.Add(message);
            }
        }

        return messages;
    }

    private async Task<List<long>> GetUpdateIds(MySqlConnection connection,
        long userId, long startId)
    {
        var messages = new List<long>();

        using var command = new MySqlCommand("SELECT id FROM messages " +
                "WHERE user_id = @user_id AND id >= @start_id ORDER BY id limit 1000",
                connection);
        command.Parameters.AddWithValue("@user_id", userId);
        command.Parameters.AddWithValue("@start_id", startId);

        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var id = reader.GetInt64(0);
                messages.Add(id);
            }
        }

        return messages;
    }
    #endregion Helpers
}
