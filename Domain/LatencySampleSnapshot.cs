using System;

namespace LaserTag.Defusal.Domain;

public sealed record LatencySampleSnapshot(TimeSpan Average, TimeSpan Minimum, TimeSpan Maximum, int SampleCount);
