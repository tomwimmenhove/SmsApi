using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using sms.Database;

namespace sms.Controllers;

[ApiController]
[Route("[controller]")]
public class SmsController : ControllerBase
{
    private const int MaxTagLength = 16;

    private readonly ILogger<SmsController> _logger;
    private readonly IBroadcaster _userBroadcast;
    private readonly SmsControllerSettings _settings;

    public SmsController(ILogger<SmsController> logger,
        IBroadcaster userBroadcast,
        IOptions<SmsControllerSettings> settings)
    {
        _logger = logger;
        _userBroadcast = userBroadcast;
        _settings = settings.Value;
    }

    private string? GetUsername() => Request.Headers[_settings.UsernameHeader].FirstOrDefault();

    private string GetClientIp() => Request.Headers["X-Forwarded-For"].FirstOrDefault() ??
        HttpContext.Connection.RemoteIpAddress?.ToString() ??
        "unknown";

    private string GetFirstClientIp() => GetClientIp().Split(",").First().Trim().Limit(45);

    private static readonly Regex _whitespaceRegex = new Regex(@"\s+");
    private Regex _validateNumberRegex = new Regex(@"^\+?\d*$", RegexOptions.Compiled);
    private Regex _antiSpamRegex = new Regex(
        @"((http|ftp|https):\/\/[\w\-_]+(\.[\w\-_]+)+([\w\-\.,@?^=%&amp;:/~\+#]*[\w\-\@?^=%&amp;/~\+#])?)",
        RegexOptions.Compiled);

    /* End point called by the servier with the SMS daemon, when a net SMS is received on the modem */
    [HttpPost("NewMessage")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(SimpleOkResponeDto))]
    public async Task<IActionResult> NewMessage([FromBody] NewMessageDto data)
    {
        _logger.LogInformation($"{GetClientIp()} - " +
            $"NewMessage: from=\"{data.From}\", to=\"{data.To}\"");

        using var db = await SmsApiDb.Create(_settings.ConnectionString);

        var tag = new string(data.Message.TakeWhile(x => !char.IsWhiteSpace(x)).ToArray()).Trim().ToUpper();
        var userId = await db.GetUserIdFromTag(tag);

        await db.AddReceivedMessage(userId, data.From, data.To, data.Message);

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

        using var db = await SmsApiDb.Create(_settings.ConnectionString);
            
        var messages = await db.GetSendMessageUpdates(startId);
        if (messages.Count > 0 || timeOut == 0)
        {
            return Ok(new SendMessageUpdateResultsDto
            {
                Messages = messages
            });
        }
            // Dispose of the managed resources held by this object.

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
            messages = await db.GetSendMessageUpdates(startId);
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

    [HttpGet("GetNumbers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GetNumbersResponeDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(SimpleErrorResponeDto))]
    public async Task<IActionResult> GetNumbers()
    {
        var username = GetUsername();
        if (username == null)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "No username given"
            });
        }

        using var db = await SmsApiDb.Create(_settings.ConnectionString);
        await db.GetOrCreateUserId(username, GetFirstClientIp());

        return Ok(new GetNumbersResponeDto
        {
            Numbers = _settings.Numbers
        });
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

        if (_antiSpamRegex.IsMatch(data.Message))
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "Message contains links"
            });
        }

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

        using var db = await SmsApiDb.Create(_settings.ConnectionString);
        var userId = await db.GetOrCreateUserId(username, GetFirstClientIp());
        await db.AddSendMessage(userId, data.From, data.To, data.Message);

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
        if (tag.Length > MaxTagLength)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = $"Tag can be no more than {MaxTagLength} characters"
            });
        }
        if (username == null)
        {
            return BadRequest(new SimpleErrorResponeDto
            {
                Message = "No username given"
            });
        }

        using var db = await SmsApiDb.Create(_settings.ConnectionString);

        try
        {
            await db.GetOrCreateUserId(username, GetFirstClientIp(), tag);
        }
        catch (MySql.Data.MySqlClient.MySqlException ex)
        {
            if (ex.Number == 1062) // Duplicate error
            {
                return BadRequest(new SimpleErrorResponeDto
                {
                    Message = $"Tag \"{tag}\" is already in use"
                });
            }

            throw;
        }

        return Ok(new SimpleOkResponeDto());
    }

    [HttpGet("GetTag")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GetTagDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(SimpleErrorResponeDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(SimpleErrorResponeDto))]
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

        using var db = await SmsApiDb.Create(_settings.ConnectionString);
        var userId = await db.GetOrCreateUserId(username, GetFirstClientIp());
        var tag = await db.GetTag(username);
        if (tag == null)
        {
            return NotFound(new SimpleErrorResponeDto
            {
                Message = "No tag set. Please use the SetTag endpoint to set a tag first."
            });
        }

        return Ok(new GetTagDto { Tag = tag });
    }

    [HttpGet("GetUpdates")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(GetUpdatesDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(SimpleErrorResponeDto))]
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

        using var db = await SmsApiDb.Create(_settings.ConnectionString);
        var userId = await db.GetOrCreateUserId(username, GetFirstClientIp());
        var tag = await db.GetTag(username);
        if (tag == null)
        {
            return NotFound(new SimpleErrorResponeDto
            {
                Message = "No tag set. Please use the SetTag endpoint to set a tag first."
            });
        }

        var messages = await db.GetUpdateIds(userId, startId);
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
            messages = await db.GetUpdateIds(userId, startId);
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

        using var db = await SmsApiDb.Create(_settings.ConnectionString);
        var userId = await db.GetOrCreateUserId(username, GetFirstClientIp());

        var message = await db.GetMessage(userId, messageId);
        if (message == null)
        {
            return NotFound(new SimpleErrorResponeDto
            {
                Message = "Message not found"
            });
        }

        return Ok(new MessageDTO
        {
            CreatedAt = message.CreatedAt,
            From = message.From,
            To = message.To,
            Message = message.Message
        });
    }
}
