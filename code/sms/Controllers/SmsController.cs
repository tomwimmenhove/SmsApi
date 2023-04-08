using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace sms.Controllers;

public class NewMessageDto
{
    [Required]
    public string From { get; set; } = default!;

    [Required]
    public string To { get; set; } = default!;

    [Required]
    public string Message { get; set; } = default!;
}

[ApiController]
[Route("[controller]")]
public class SmsController : ControllerBase
{
    private readonly ILogger<SmsController> _logger;

    public SmsController(ILogger<SmsController> logger)
    {
        _logger = logger;
    }

    private string _connectionString = "Server=localhost;Database=sms_api;Uid=tom;";

    private string? GetUsername() => Request.Headers["X-RapidAPI-User"].FirstOrDefault();

    private static readonly Regex _whitespaceRegex = new Regex(@"\s+");

    [HttpPost("NewMessage")]
    public async Task<IActionResult> NewMessage([FromBody] NewMessageDto data)
    {
        var tag = new string(data.Message.TakeWhile(x => !char.IsWhiteSpace(x)).ToArray()).Trim().ToUpper();
        
        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            var userId = -1;
            using (var command = new MySqlCommand("SELECT id FROM users WHERE tag = @tag",
                connection))
            {
                command.Parameters.AddWithValue("@tag", tag);

                var result = await command.ExecuteScalarAsync();
                if (result != null)
                {
                    userId = Convert.ToInt32(result);
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
                using (var command = new MySqlCommand("INSERT INTO messages "+
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
            }
        }

        return Ok(new { success = true });
    }

    [HttpGet("SetTag")]
    public async Task<IActionResult> SetTag([FromQuery, Required] string tag)
    {
        tag = tag.Trim().ToUpper();
        if (tag.Length > 6)
        {
            return BadRequest(new { success = false, message = "Tag can be no more than 16 characters"});
        }
        var username = GetUsername();
        if (username == null)
        {
            return BadRequest(new { success = false, message = "No username given"});
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
                    return BadRequest(new { success = false, message = $"Tag \"{tag}\" is already in use" });
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

        return Ok(new { success = true });
    }

    [HttpGet("GetTag")]
    public async Task<IActionResult> SetTag()
    {
        var username = GetUsername();
        if (username == null)
        {
            return BadRequest(new { success = false, message = "No username given"});
        }

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            using (var command = new MySqlCommand("SELECT tag FROM users " +
                "WHERE username = @username",
                connection))
            {
                command.Parameters.AddWithValue("@username", username);
                var result = await command.ExecuteScalarAsync();
                if (result == null)
                {
                    return NotFound(new { success = false, message = "No tag set"});
                }

                return Ok( new { success = true, tag = (string) result });
            }
        }
    }

    [HttpGet("GetUpdates")]
    public async Task<IActionResult> GetUpdates([FromQuery, Required] int start_id)
    {
        var username = GetUsername();
        if (username == null)
        {
            return BadRequest(new { success = false, message = "No username given"});
        }

        var messages = new List<int>();

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            var userId = await GetOrCreateUserId(connection, username);

            using (var command = new MySqlCommand("SELECT id FROM messages " +
                "WHERE user_id = @user_id AND id >= @start_id ORDER BY id limit 1000",
                connection))
            {
                command.Parameters.AddWithValue("@user_id", userId);
                command.Parameters.AddWithValue("@start_id", start_id);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var id = reader.GetInt32(0);
                        messages.Add(id);
                    }
                }
            }
        }

        return Ok(new { success = true, messages = messages });
    }

    [HttpGet("GetMessage")]
    public async Task<IActionResult> GetMessage([FromQuery, Required] int message_id)
    {
        var username = GetUsername();
        if (username == null)
        {
            return BadRequest(new { success = false, message = "No username given"});
        }

        var messages = new List<int>();

        using (var connection = new MySqlConnection(_connectionString))
        {
            await connection.OpenAsync();

            var userId = await GetOrCreateUserId(connection, username);

            string sqlQuery = "SELECT created_at, number_from, number_to, message FROM messages WHERE user_id = @user_id AND id = @message_id limit 1";

            using (var command = new MySqlCommand(sqlQuery, connection))
            {
                command.Parameters.AddWithValue("@user_id", userId);
                command.Parameters.AddWithValue("@message_id", message_id);

                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        return Ok(new
                        {
                            success = true,
                            createdAt = reader.GetDateTime(0),
                            from = reader.GetString(1),
                            to = reader.GetString(2),
                            message = reader.GetString(3)
                        });
                    }
                }
            }
        }

        return NotFound(new { success = false, message = "Message not found"});
    }

#region Helpers
    private async Task<UInt32?> GetUserId(MySqlConnection connection, string username)
    {
        using(var command = new MySqlCommand("SELECT id FROM users WHERE username = @username",
            connection))
        {
            command.Parameters.AddWithValue("@username", username);
            return (UInt32?) await command.ExecuteScalarAsync();
        }
    }

    private async Task<UInt32> GetOrCreateUserId(MySqlConnection connection, string username)
    {
        using(var command = new MySqlCommand("UPDATE users SET last_access = CURRENT_TIMESTAMP " +
            "WHERE username = @username",
            connection))
        {
            command.Parameters.AddWithValue("@username", username);
            await command.ExecuteNonQueryAsync();
        }

        var userId = await GetUserId(connection, username);
        if (userId != null)
        {
            return userId.Value;
        }

        using (var command = new MySqlCommand("INSERT INTO users (username, tag)" +
            "VALUES (@username, @tag)",
            connection))
        {
            var tag = _whitespaceRegex.Replace(username, "");
            tag = tag.Substring(0, Math.Min(16, tag.Length)).ToUpper();
            command.Parameters.AddWithValue("@tag", tag);
            command.Parameters.AddWithValue("@username", username);

            await command.ExecuteNonQueryAsync();
        }

        userId = await GetUserId(connection, username);

        return userId.Value;
    }
#endregion Helpers
}
