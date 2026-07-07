using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.EasyTierPlugin.Scaffolding;
using System;

namespace PCL.EasyTierPlugin.Test;

[TestClass]
public class LobbyCodeGenerateTest
{
    [TestMethod]
    public void GenerateTest()
    {
        var code = LobbyCodeGenerator.Generate();

        Assert.IsFalse(string.IsNullOrWhiteSpace(code.FullCode));
    }

    [TestMethod]
    public void ParseTest()
    {
        var code = LobbyCodeGenerator.Generate();
        Console.WriteLine($"Try to parse: {code.FullCode}");

        var success = LobbyCodeGenerator.TryParse(code.FullCode, out _);

        Assert.IsTrue(success);
    }
}
