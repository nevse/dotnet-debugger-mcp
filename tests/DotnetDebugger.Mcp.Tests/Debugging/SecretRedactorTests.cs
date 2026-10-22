using DotnetDebugger.Mcp.Debugging;

namespace DotnetDebugger.Mcp.Tests.Debugging;

/// <summary>
/// What reaches the model in place of a secret. Values are written as the debugger shows them: a
/// string in quotes, null as the bare word.
/// </summary>
[TestClass]
public class SecretRedactorTests
{
    private static SecretRedactor On() => new(enabled: true);

    [TestMethod]
    [DataRow("password")]
    [DataRow("Password")]
    [DataRow("dbPassword")]
    [DataRow("passwd")]
    [DataRow("pwd")]
    [DataRow("clientSecret")]
    [DataRow("accessToken")]
    [DataRow("api_key")]
    [DataRow("ApiKey")]
    [DataRow("ConnectionString")]
    [DataRow("credentials")]
    [DataRow("privateKey")]
    [DataRow("AccessKey")]
    [DataRow("Authorization")]
    public void Value_AStringNamedLikeASecret_IsMaskedWhole(string name)
    {
        var redactor = On();

        var shown = redactor.Value(name, "string", "\"hunter2\"", 0);

        Assert.AreEqual("[redacted: 7 chars]", shown);
        Assert.AreEqual(1, redactor.Count);
    }

    [TestMethod]
    [DataRow("System.String")]
    [DataRow("string?")]
    public void Value_TheOtherNamesForString_AreMaskedToo(string type)
    {
        Assert.AreEqual("[redacted: 7 chars]", On().Value("password", type, "\"hunter2\"", 0));
    }

    [TestMethod]
    [DataRow("tokenCount", "int", "5", 0)]
    [DataRow("token", "System.Threading.CancellationToken", "{System.Threading.CancellationToken}", 0)]
    [DataRow("credentials", "MyApp.Credentials", "{MyApp.Credentials}", 1001)]
    [DataRow("author", "string", "\"Ada\"", 0)]
    [DataRow("label", "string", "\"n=1\"", 0)]
    public void Value_WhatOnlyLooksLikeASecret_IsLeftAlone(string name, string type, string value, int reference)
    {
        var redactor = On();

        Assert.AreEqual(value, redactor.Value(name, type, value, reference));
        Assert.AreEqual(0, redactor.Count);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("\"\"")]
    public void Value_ASecretThatIsNotSet_ShowsAsItIs(string value)
    {
        // Whether the password is set at all is what a debugging session most often needs to know
        var redactor = On();

        Assert.AreEqual(value, redactor.Value("password", "string", value, 0));
        Assert.AreEqual(0, redactor.Count);
    }

    [TestMethod]
    public void Value_WithAnInnocentName_StillHasItsPatternsMasked()
    {
        var shown = On().Value("header", "string", "\"Bearer abcdefghijklmnop\"", 0);

        Assert.AreEqual("\"Bearer [redacted: 16 chars]\"", shown);
    }

    [TestMethod]
    public void Value_WhenDisabled_IsReturnedUnchanged()
    {
        var redactor = new SecretRedactor(enabled: false);

        Assert.AreEqual("\"hunter2\"", redactor.Value("password", "string", "\"hunter2\"", 0));
        Assert.AreEqual("Bearer abcdefghijklmnop", redactor.Text("Bearer abcdefghijklmnop"));
        Assert.AreEqual(0, redactor.Count);
    }

    [TestMethod]
    [DataRow("Authorization: Bearer abcdefghijklmnop", "Authorization: Bearer [redacted: 16 chars]")]
    [DataRow("Basic dXNlcjpwYXNzd29yZA==", "Basic [redacted: 20 chars]")]
    [DataRow("jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl ok",
        "jwt [redacted: 49 chars] ok")]
    [DataRow("Server=db;User Id=sa;Password=hunter2;Encrypt=true",
        "Server=db;User Id=sa;Password=[redacted: 7 chars];Encrypt=true")]
    [DataRow("Host=db;Pwd=hunter2", "Host=db;Pwd=[redacted: 7 chars]")]
    [DataRow("https://acct.blob.core.windows.net/c?sv=2022&sig=abcDEF123%3D&se=1",
        "https://acct.blob.core.windows.net/c?sv=2022&sig=[redacted: 12 chars]&se=1")]
    [DataRow("key sk-abcdefghijklmnopqrstuvwx", "key [redacted: 27 chars]")]
    [DataRow("ghp_abcdefghijklmnopqrstuvwxyz0123456789", "[redacted: 40 chars]")]
    [DataRow("AKIAABCDEFGHIJKLMNOP", "[redacted: 20 chars]")]
    public void Text_RecognisableSecrets_AreMaskedWhereTheyOccur(string text, string expected)
    {
        Assert.AreEqual(expected, On().Text(text));
    }

    [TestMethod]
    public void Text_APemPrivateKey_IsMaskedWhole()
    {
        const string key = "-----BEGIN RSA PRIVATE KEY-----\nMIIBOgIBAAJBAKj34GkxFhD90vcNLYLInFEX6Ppy1tPf9Cnzj4p4WGeKLs1Pt8Qu\n-----END RSA PRIVATE KEY-----";

        Assert.AreEqual($"loaded [redacted: {key.Length} chars]", On().Text($"loaded {key}"));
    }

    [TestMethod]
    public void Text_SeveralSecrets_AreEachMaskedAndCounted()
    {
        var redactor = On();

        var shown = redactor.Text("Password=a1b2c3 and Bearer abcdefghijklmnop");

        Assert.AreEqual("Password=[redacted: 6 chars] and Bearer [redacted: 16 chars]", shown);
        Assert.AreEqual(2, redactor.Count);
    }

    [TestMethod]
    [DataRow("tick 5")]
    [DataRow("PID=1234")]
    [DataRow("The token was rejected")]
    [DataRow("password reset sent")]
    public void Text_OrdinaryOutput_IsLeftAlone(string text)
    {
        Assert.AreEqual(text, On().Text(text));
    }

    [TestMethod]
    [DataRow("password", "password")]
    [DataRow("config.Db.Password", "Password")]
    [DataRow("this.token", "token")]
    [DataRow("  apiKey  ", "apiKey")]
    [DataRow("GetSecret()", null)]
    [DataRow("items[0]", null)]
    [DataRow("a + password", null)]
    [DataRow("42", null)]
    public void LastIdentifier_IsTheMemberTheResultIsRead(string expression, string? expected)
    {
        Assert.AreEqual(expected, SecretRedactor.LastIdentifier(expression));
    }
}
