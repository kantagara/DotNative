using System.Xml.Linq;

internal static partial class Preview
{
    private static string WriteSimulatorEntitlements(string output, string bundle)
    {
        var path = Path.Combine(output, "Simulator.entitlements");
        new XDocument(
            new XElement(
                "plist",
                new XAttribute("version", "1.0"),
                new XElement(
                    "dict",
                    new XElement("key", "application-identifier"),
                    new XElement("string", bundle),
                    new XElement("key", "keychain-access-groups"),
                    new XElement("array", new XElement("string", bundle)),
                    new XElement("key", "aps-environment"),
                    new XElement("string", "development")
                )
            )
        ).Save(path);
        return path;
    }

    private static async Task BuildLaunchScreen(string app, string product, string sdk)
    {
        var source = Path.Combine(app, "Platform/iOS/LaunchScreen.storyboard");
        if (!File.Exists(source))
            return;
        await Exec(
            "xcrun",
            [
                "--sdk",
                sdk,
                "ibtool",
                "--compile",
                Path.Combine(product, "LaunchScreen.storyboardc"),
                source,
            ]
        );
        var assets = Path.Combine(app, "Platform/iOS/LaunchAssets.xcassets");
        if (Directory.Exists(assets))
            await Exec(
                "xcrun",
                [
                    "--sdk",
                    sdk,
                    "actool",
                    assets,
                    "--compile",
                    product,
                    "--platform",
                    sdk,
                    "--minimum-deployment-target",
                    "16.0",
                    "--target-device",
                    "iphone",
                ]
            );
        var plist = Path.Combine(product, "Info.plist");
        var document = XDocument.Load(plist);
        var dict = document.Root!.Element("dict")!;
        var automatic = dict.Elements("key").FirstOrDefault(key => key.Value == "UILaunchScreen");
        if (automatic is not null)
        {
            automatic.NextNode?.Remove();
            automatic.Remove();
        }
        dict.Add(
            new XElement("key", "UILaunchStoryboardName"),
            new XElement("string", "LaunchScreen")
        );
        document.Save(plist);
    }
}
