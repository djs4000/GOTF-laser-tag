using System;

namespace LaserTag.Defusal.Domain;

/// <summary>
/// Represents a user-crafted payload plus metadata about the most recent send attempt.
/// </summary>
public sealed class DebugPayloadTemplate
{
    public string Name { get; set; } = "Ad-hoc payload";

    public DebugPayloadType PayloadType { get; set; } = DebugPayloadType.Combined;

    public string JsonBody { get; set; } = string.Empty;

    public DateTimeOffset? LastSentUtc { get; set; }

    public bool? LastSendSucceeded { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastMessage { get; set; }
}

public enum DebugPayloadType
{
    Match,
    Prop,
    Combined
}

public sealed record DebugPayloadResult(bool Success, int? StatusCode, string Message);

public sealed class DebugPayloadValidationException : Exception
{
    public DebugPayloadValidationException(string message)
        : base(message)
    {
    }
}
