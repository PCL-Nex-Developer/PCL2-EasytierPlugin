using PCL.EasyTierPlugin.Scaffolding.Client.Models;
using System;

namespace PCL.EasyTierPlugin.Scaffolding.Server;

public record TrackedPlayerProfile
{
    public required PlayerProfile Profile { get; set; }
    public required DateTime LastSeenUtc { get; set; }
};