using PCL.EasyTierPlugin.Scaffolding.Client.Models;
using System.Collections.Generic;

namespace PCL.EasyTierPlugin.Scaffolding;

public class PlayerListHandler
{
    public static List<PlayerProfile> Sort(IReadOnlyList<PlayerProfile> list)
    {
        var sorted = new List<PlayerProfile>();
        foreach (var profile in list)
        {
            if (profile.Kind == PlayerKind.HOST)
            {
                sorted.Insert(0, profile);
            }
            else
            {
                sorted.Add(profile);
            }
        }
        return sorted;
    }
}