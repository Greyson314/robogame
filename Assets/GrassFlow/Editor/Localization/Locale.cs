using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Reflection;

namespace GrassFlow.Localization {

    public abstract class Locale {


        public static void SetLang(Type localeType) {
            Locale locale = (Locale)Activator.CreateInstance(localeType);
            current = locale;
            EditorPrefs.SetString("GF_LANG", locale.isoCode);
        }

        public static void LoadLang() {
            var iso = EditorPrefs.GetString("GF_LANG", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
            var localeType = LanguageSelectorWindow.GetAllInheritedTypes(typeof(Locale)).FirstOrDefault(x => 
                GetIsoCodeFromType(x) == iso
            );
            if (localeType == null) {
                localeType = typeof(English);
            }
            //Debug.Log(localeType);
            Locale locale = (Locale)Activator.CreateInstance(localeType);
            current = locale;
        }

        private const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
        internal static string GetIsoCodeFromType(Type type) {
            return (string)type.GetProperty("isoCodeStatic", flags).GetValue(null);
        }

        internal static Locale current = new English();


        public static string GetName(string memberName) {
            if (current.names.TryGetValue(memberName, out var name)) {
                return name;
            }
            else {
                return English.GetName(memberName);
            }
        }

        public static string GetTooltip(string memberName) {
            if (current.tooltips.TryGetValue(memberName, out var name)) {
                return name;
            }
            else {
                return English.GetTooltip(memberName);
            }
        }

        public static string GetMatTooltip(string memberName) {
            if (current.materialTooltips.TryGetValue(memberName, out var name)) {
                return name;
            }
            else {
                return English.GetMatTooltip(memberName);
            }
        }

        public static string GetMsg(string memberName) {
            if (current.messages.TryGetValue(memberName, out var name)) {
                return name;
            }
            else {
                return English.GetMsg(memberName);
            }
        }

        internal string isoCodeStatic { get; }

        internal abstract string isoCode { get; }
        internal abstract Dictionary<string, string> names { get; }
        internal abstract Dictionary<string, string> tooltips { get; }
        internal abstract Dictionary<string, string> materialTooltips { get; }
        internal abstract Dictionary<string, string> messages { get; }


        [UnityEditor.MenuItem("CONTEXT/GrassFlowRenderer/Change Language")]
        public static void ChangeLang() {
            LanguageSelectorWindow window =
                (LanguageSelectorWindow)EditorWindow.GetWindow(typeof(LanguageSelectorWindow), true, Locale.GetMsg("languageSelectorTitle"));
            window.Show();
        }
    }


    public class LanguageSelectorWindow : EditorWindow {
        int selected;

        string[] options;
        Type[] types;
        public LanguageSelectorWindow() {

            types = GetAllInheritedTypes(typeof(Locale)).OrderBy(x => Locale.GetIsoCodeFromType(x)).ToArray();
            options = types.Select(x => {
                var culture = CultureInfo.GetCultureInfo(Locale.GetIsoCodeFromType(x));
                //Debug.Log(x);
                //Debug.Log(culture);
                return culture.NativeName;
            }).ToArray();
            selected = Array.IndexOf(options, CultureInfo.GetCultureInfo(Locale.current.isoCode).NativeName);

            minSize = new Vector2(250, 100);
            maxSize = minSize;
        }

        internal static IEnumerable<Type> GetAllInheritedTypes(Type baseType) {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => baseType.IsAssignableFrom(type) && type != baseType);
        }

        void OnGUI() {
            EditorGUILayout.LabelField(Locale.GetMsg("selectLanguagePrompt"), EditorStyles.boldLabel);

            selected = EditorGUILayout.Popup(Locale.GetMsg("languageLabel"), selected, options);
            GUILayout.Space(20);

            if (GUILayout.Button(Locale.GetMsg("confirmSelectionButton"))) {
                Locale.SetLang(types[selected]);
                Close();
            }
        }
    }
}