#if MACCATALYST
using Helix.Application.Abstractions.Startup;
using Helix.Infrastructure.Platform;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace Helix.Infrastructure.Startup;

[SupportedOSPlatform("maccatalyst")]
internal sealed class MacStartupService : IStartupService
{
    private const string AgentLabel = "com.hilthon.helix.startup";

    private static readonly string LaunchAgentsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library",
        "LaunchAgents");

    private static readonly string AgentPath = Path.Combine(LaunchAgentsFolder, $"{AgentLabel}.plist");

    public bool IsSetToStartup()
    {
        return File.Exists(AgentPath);
    }

    public void ToggleStartup(bool value)
    {
        if (value)
        {
            CreateLaunchAgent();
        }
        else
        {
            DeleteLaunchAgent();
        }
    }

    private static void CreateLaunchAgent()
    {
        string bundlePath = MacBundle.BundlePath;

        try
        {
            Directory.CreateDirectory(LaunchAgentsFolder);

            var plist = new XDocument(
                new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN",
                    "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
                new XElement("plist",
                    new XAttribute("version", "1.0"),
                    new XElement("dict",
                        new XElement("key", "Label"),
                        new XElement("string", AgentLabel),
                        new XElement("key", "ProgramArguments"),
                        new XElement("array",
                            new XElement("string", "/usr/bin/open"),
                            new XElement("string", "-a"),
                            new XElement("string", bundlePath)),
                        new XElement("key", "RunAtLoad"),
                        new XElement("true"))));

            plist.Save(AgentPath);
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to create the login item.", ex);
        }
    }

    private static void DeleteLaunchAgent()
    {
        try
        {
            if (File.Exists(AgentPath))
            {
                File.Delete(AgentPath);
            }
        }
        catch (Exception ex)
        {
            throw new IOException("Failed to remove the login item.", ex);
        }
    }
}
#endif
