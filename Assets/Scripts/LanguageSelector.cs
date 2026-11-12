using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;

public class LanguageSelector : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;

    private IEnumerator Start()
    {
        // Espera a que el sistema de localización termine de inicializarse
        yield return LocalizationSettings.InitializationOperation;

        var locales = LocalizationSettings.AvailableLocales.Locales;
        var options = new List<string>();
        int selected = 0;

        for (int i = 0; i < locales.Count; i++)
        {
            options.Add(locales[i].LocaleName);
            if (locales[i] == LocalizationSettings.SelectedLocale)
                selected = i;
        }

        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.SetValueWithoutNotify(selected);

        dropdown.onValueChanged.AddListener(index =>
        {
            LocalizationSettings.SelectedLocale = locales[index];
        });
    }
}