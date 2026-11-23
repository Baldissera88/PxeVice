namespace PxeVice.Tests
{
    using System;

    public static class ThemeTests
    {
        public static void Run()
        {
            T.Equal("Vice", Palettes.Default, "Vice is the default");
            T.Equal("Vice", Palettes.SavedName(null), "nothing saved: Vice");
            T.Equal("Dark", Palettes.SavedName(" Dark "), "saved name is trimmed");
            T.Equal(7, Palettes.Choices.Length, "System and six themes");
            foreach (string n in Palettes.Choices)
            {
                if (n == "System") { T.Check(Palettes.Find(n) == null, "System has no palette"); continue; }
                Palette p = Palettes.Find(n.ToUpperInvariant());
                T.Check(p != null && p.Name == n, "palette " + n + " found without regard to case");
            }
            T.Check(Palettes.Find("Terminal").Corners == 0f && Palettes.Find("Terminal").MonoNumbers, "Terminal: square corners, mono numbers");
            T.Check(Palettes.Find("Amber").Corners == 0f && Palettes.Find("Amber").MonoNumbers, "Amber: square corners, mono numbers");
            T.Check(Palettes.Find("Synthwave").IsDark && Palettes.Find("Synthwave").Corners > 0f, "Synthwave is dark with rounded corners");
            T.Check(Palettes.Find("Light").Text3 == 0x636A74, "Light's secondary text is the darker one");
            T.Check(Palettes.Find("Nonsense") == null, "unknown name: no palette");
            T.Equal(6, Palettes.All().Count, "six palettes");
        }
    }
}
