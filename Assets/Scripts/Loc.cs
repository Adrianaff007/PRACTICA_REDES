using UnityEngine.Localization.Settings;

/// <summary>
/// Acceso a las String Tables. Devuelve el texto con el idioma activo.
/// </summary>
public static class Loc
{
    public static string Get(string table, string key, params object[] args)
    {
        return LocalizationSettings.StringDatabase.GetLocalizedString(
            table, key, null, FallbackBehavior.UseProjectSettings, args);
    }
}