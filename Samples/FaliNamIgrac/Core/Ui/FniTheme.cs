using DotNative;

namespace FaliNamIgrac;

internal static class FniTheme
{
    public static ThemeData Light { get; } =
        new()
        {
            Primary = new(60, 94, 245),
            OnPrimary = Color.White,
            Background = Color.White,
            SecondaryBackground = new(245, 246, 247),
            Surface = new(246, 246, 246),
            Foreground = new(38, 38, 38),
            MutedForeground = new(110, 110, 110),
            Error = new(184, 55, 55),
            ErrorSurface = new(255, 226, 226),
            Text = new()
            {
                Body = new() { Font = FniAssets.Font() },
                Title = new()
                {
                    Font = FniAssets.Font("Bold"),
                    Size = 24,
                    Weight = FontWeight.Bold,
                },
                Label = new()
                {
                    Font = FniAssets.Font("Medium"),
                    Size = 14,
                    Weight = FontWeight.Medium,
                },
                Strong = new()
                {
                    Font = FniAssets.Font("ExtraBold"),
                    Weight = FontWeight.ExtraBold,
                },
                Icon = new() { Font = FniAssets.Font("Icons"), Size = 24 },
            },
        };

    public static ThemeData Dark { get; } =
        Light with
        {
            Mode = ThemeMode.Dark,
            Background = new(13, 13, 13),
            SecondaryBackground = new(22, 22, 22),
            Surface = new(37, 37, 37),
            Foreground = new(217, 217, 217),
            MutedForeground = new(160, 160, 160),
            Error = new(238, 109, 109),
        };
}
