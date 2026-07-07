using System;
using System.Net;

namespace PCL.EasyTierPlugin.Lobby;

public record BroadcastRecord(string Desc, IPEndPoint Address, DateTime FoundAt);
