namespace CharacterHeight;

/// <summary>
/// Character-creator heights in centimeters. The slider is linear from 0 to 100.
/// Clan only changes Hyur (Midlander vs Highlander). Lalafell share one range.
/// </summary>
public static class HeightTable {
    public static bool TryGetRange(byte race, byte tribe, byte sex, out float minCm, out float maxCm) {
        minCm = 0f;
        maxCm = 0f;
        var female = sex == 1;

        (minCm, maxCm) = race switch {
            1 when tribe == 2 && !female => (184.8f, 200.2f),
            1 when tribe == 2 && female => (173.1f, 187.6f),
            1 when !female => (168.0f, 182.0f),
            1 when female => (157.4f, 170.5f),
            2 when !female => (194.1f, 209.8f),
            2 when female => (183.5f, 198.4f),
            3 => (86.9f, 97.0f),
            4 when !female => (159.2f, 173.2f),
            4 when female => (149.7f, 162.2f),
            5 when !female => (213.5f, 230.4f),
            5 when female => (192.0f, 222.7f),
            6 when !female => (203.0f, 217.0f),
            6 when female => (146.0f, 158.5f),
            7 when !female => (196.2f, 212.9f),
            7 when female => (184.4f, 199.4f),
            8 when !female => (172.2f, 186.5f),
            8 when female => (178.8f, 191.4f),
            _ => (0f, 0f),
        };

        return maxCm > minCm;
    }

    public static bool TryCreatorCentimeters(byte race, byte tribe, byte sex, byte height, out float centimeters) {
        centimeters = 0f;
        if (!TryGetRange(race, tribe, sex, out var minCm, out var maxCm)) return false;
        var slider = Math.Clamp(height, (byte)0, (byte)100) / 100f;
        centimeters = minCm + (maxCm - minCm) * slider;
        return true;
    }
}
