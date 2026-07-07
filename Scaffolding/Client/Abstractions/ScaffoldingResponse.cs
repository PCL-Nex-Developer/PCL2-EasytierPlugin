using System;

namespace PCL.EasyTierPlugin.Scaffolding.Client.Abstractions;

public record ScaffoldingResponse(byte Status, ReadOnlyMemory<byte> Body);