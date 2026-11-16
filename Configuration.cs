using Dalamud.Configuration;

namespace CharacterHeight;

public enum HeightUnit {
    Centimeters,
    Meters,
    FeetInches,
    Millimeters,
    Kilometers,
    Miles,
}

public enum PartyLabelSide {
    Auto,
    Left,
    Right,
}

public sealed class Configuration : IPluginConfiguration {
    public int Version { get; set; } = 1;

    public HeightUnit Unit { get; set; } = HeightUnit.Centimeters;
    public int Decimals { get; set; } = 1;

    /// <summary>
    /// When on, the unit choice is metric or imperial and the label steps at ordinary boundaries.
    /// </summary>
    public bool CondenseHugeSizes { get; set; }

    /// <summary>
    /// Multiplier for height label size. 1 matches the name text.
    /// </summary>
    public float TextScale { get; set; } = 1f;
    public PartyLabelSide Side { get; set; } = PartyLabelSide.Auto;
    public bool ShowParty { get; set; } = true;
    public bool ShowTarget { get; set; } = true;

    /// <summary>
    /// Briefly glows a height green or red, with an arrow, when the shown value changes.
    /// </summary>
    public bool FlashChanges { get; set; }

    /// <summary>
    /// Shows the local player's height on the server info bar and hides it on their party row.
    /// </summary>
    public bool ShowOwnOnWorldBar { get; set; }
    public bool HideInCombat { get; set; }
    public bool HideInCutscene { get; set; } = true;

    /// <summary>
    /// Multiplies by the racial / Penumbra / Special Snowflakes scale at DrawObject+0x2A4.
    /// Leave this on when an unmodded character reads about 1. Turn it off if that scale already includes the height slider.
    /// </summary>
    public bool ApplyModelScale { get; set; } = true;

    /// <summary>
    /// Multiplies by DrawObject.Object.Scale.Z, the plugin / SizeChange scale.
    /// </summary>
    public bool ApplyCustomScale { get; set; } = true;

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
