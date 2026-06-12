namespace Rundan.Server.Data.Entities;

/// <summary>A persisted key/value app setting that can be edited from the UI and overrides the matching
/// server/env configuration (e.g. the Spotify app Client ID), so a host can set it without an Azure restart.</summary>
public class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
