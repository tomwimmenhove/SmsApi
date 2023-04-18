using MySql.Data.MySqlClient;

namespace sms.Database;

public class SmsApiDb : IDisposable
{
    private bool _disposed;

    private MySqlConnection _connection = default!;

    private SmsApiDb(MySqlConnection connection) =>
        _connection = connection;

    public static async Task<SmsApiDb> Create(string connectionString)
    {
        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        return new SmsApiDb(connection);
    }

    public async Task<long> GetUserIdFromTag(string tag)
    {
        using var command = new MySqlCommand(
            "SELECT id FROM users WHERE tag = @tag",
            _connection);

        command.Parameters.AddWithValue("@tag", tag);

        var result = await command.ExecuteScalarAsync();
        
        return result is long id ? id : -1;
    }

    public async Task<long> GetUserIdFromUsername(string username)
    {
        using var command = new MySqlCommand(
            "SELECT id FROM users WHERE username = @username",
            _connection);

        command.Parameters.AddWithValue("@username", username);

        var result = await command.ExecuteScalarAsync();
        
        return result is long id ? id : -1;
    }

    private async Task<long> GetUserId(string username, MySqlTransaction transaction)
    {
        using (var command = new MySqlCommand(
            "SELECT id FROM users WHERE username = @username",
            _connection, transaction))
        {
            command.Parameters.AddWithValue("@username", username);

            var result = await command.ExecuteScalarAsync();

            return result is long id ? id : -1;
        }
    }

    private async Task UpdateUser(long user_id, string ipAddress, string? tag,
        MySqlTransaction transaction)
    {
        var query = "UPDATE users SET " +
            "last_access = UTC_TIMESTAMP, " +
            "ip_address = @ip_address " +
            (tag != null ? ", tag = @tag " : string.Empty) +
            "WHERE id = @id";

        using (var command = new MySqlCommand(query, _connection, transaction))
        {
            command.Parameters.AddWithValue("@id", user_id);
            command.Parameters.AddWithValue("@ip_address", ipAddress);
            if (tag != null)
            {
                command.Parameters.AddWithValue("@tag", tag);
            }

            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task<long> CreateUser(string username, string ipAddress, string? tag,
        MySqlTransaction transaction)
    {
        using (var command = new MySqlCommand(
            "INSERT INTO users (username, ip_address, tag) " +
            "VALUES (@username, @ip_address, @tag)",
             _connection, transaction))
        {
            command.Parameters.AddWithValue("@username", username);
            command.Parameters.AddWithValue("@ip_address", ipAddress);
            command.Parameters.AddWithValue("@tag", tag);
            await command.ExecuteNonQueryAsync();

            return command.LastInsertedId;
        }
    }

    public async Task<long> GetOrCreateUserId(string username, string ipAddress, string? tag = null)
    {
        using var transaction = await _connection.BeginTransactionAsync();
        try
        {
            var userId = await GetUserId(username, transaction);
            if (userId == -1)
            {
                userId = await CreateUser(username, ipAddress, tag, transaction);
            }
            else
            {
                await UpdateUser(userId, ipAddress, tag, transaction);
            }

            await transaction.CommitAsync();

            return userId;
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();

            throw;
        }
    }

    public async Task<List<long>> GetUpdateIds(long userId, long startId)
    {
        var messages = new List<long>();

        using var command = new MySqlCommand("SELECT id FROM messages " +
                "WHERE user_id = @user_id AND id >= @start_id ORDER BY id limit 1000",
                _connection);
        command.Parameters.AddWithValue("@user_id", userId);
        command.Parameters.AddWithValue("@start_id", startId);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt64(0);
            messages.Add(id);
        }

        return messages;
    }

    public async Task<List<SendMessageUpdateDto>> GetSendMessageUpdates(long startId)
    {
        var messages = new List<SendMessageUpdateDto>();

        using var command = new MySqlCommand(
            "SELECT id, user_id, number_from, number_to, message, created_at FROM send_messages " +
            "WHERE id >= @start_id ORDER BY id limit 1000",
            _connection);
        command.Parameters.AddWithValue("@start_id", startId);

        using var reader = await command.ExecuteReaderAsync();
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

        return messages;
    }

    public async Task<string?> GetTag(string username)
    {
        using var command = new MySqlCommand("SELECT tag FROM users " +
            "WHERE username = @username",
            _connection);

        command.Parameters.AddWithValue("@username", username);
        var result = await command.ExecuteScalarAsync();

        return result as string;
    }

    private async Task AddMessage(string table,
        long userId, string from, string to, string message)
    {
        using var command = new MySqlCommand($"INSERT INTO {table} " +
            "(user_id, number_from, number_to, message)" +
            "VALUES (@userId, @number_from, @number_to, @message)",
            _connection);

        command.Parameters.AddWithValue("@userId", userId);
        command.Parameters.AddWithValue("@number_from", from);
        command.Parameters.AddWithValue("@number_to", to);
        command.Parameters.AddWithValue("@message", message);

        await command.ExecuteNonQueryAsync();
    }

    public async Task AddSendMessage(long userId, string from, string to, string message) =>
        await AddMessage("send_messages", userId, from, to, message);

    public async Task AddReceivedMessage(long userId, string from, string to, string message) =>
        await AddMessage("messages", userId, from, to, message);

    public async Task<GetMessageDTO?> GetMessage(long userId, long messageId)
    {
        using var command = new MySqlCommand("SELECT created_at, number_from, number_to, message "
            + "FROM messages WHERE user_id = @user_id AND id = @message_id LIMIT 1",
            _connection);
        command.Parameters.AddWithValue("@user_id", userId);
        command.Parameters.AddWithValue("@message_id", messageId);

        using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new GetMessageDTO
        {
            CreatedAt = reader.GetDateTime(0),
            From = reader.GetString(1),
            To = reader.GetString(2),
            Message = reader.GetString(3)
        };
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (!_disposed)
            {
                _connection.Dispose();
                _disposed = true;
            }
        }
    }
}
