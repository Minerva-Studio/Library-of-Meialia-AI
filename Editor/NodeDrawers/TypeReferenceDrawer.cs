using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Amlos.AI.Editor
{
    internal class TypeReferenceDrawer
    {
        public enum Mode
        {
            search,
            dropDown,
        }

        private const float COMPONENT_REFERENCE_BACKGROUND_COLOR = 32f / 255f;
        private const string Label = "Class Full Name";
        private static Dictionary<Type, Type[]> allComponentClasses = new();

        private Mode mode;
        private TypeReference typeReference;
        private GUIContent label;
        private Vector2 listRect;
        private bool expanded;
        private IEnumerable<string> options;

        public TypeReference TypeReference { get => typeReference; set => typeReference = value; }
        public Type[] MatchedClasses { get => GetAllMatchedType(); }

        public TypeReferenceDrawer(TypeReference tr, string labelName) : this(tr, new GUIContent(labelName)) { }
        public TypeReferenceDrawer(TypeReference tr, GUIContent label)
        {
            this.typeReference = tr;
            this.label = label;
        }

        public void Reset(TypeReference typeReference, string labelName) => Reset(typeReference, new GUIContent(labelName));
        public void Reset(TypeReference typeReference, GUIContent label)
        {
            this.typeReference = typeReference;
            this.label = label;
            this.options = null;
        }

        public void Draw()
        {
            EditorGUILayout.BeginVertical();
            expanded = EditorGUILayout.Foldout(expanded, label + (expanded ? "" : ":\t" + TypeReference.ReferType?.FullName));
            if (expanded)
            {
                EditorGUI.indentLevel++;
                var color = GUI.backgroundColor;
                GUI.backgroundColor = Color.white * COMPONENT_REFERENCE_BACKGROUND_COLOR;
                var colorStyle = new GUIStyle();
                colorStyle.normal.background = Texture2D.whiteTexture;

                GUILayout.BeginVertical(colorStyle);
                GUILayout.BeginHorizontal();
                switch (mode)
                {
                    case Mode.dropDown:
                        DrawDropdown();
                        break;
                    case Mode.search:
                        DrawSearch();
                        break;
                    default:
                        break;
                }
                EndCheck();


                mode = (Mode)EditorGUILayout.EnumPopup(mode, GUILayout.MaxWidth(100));
                GUILayout.EndHorizontal();
                DrawAssemblyFullName();
                GUILayout.EndVertical();

                GUI.backgroundColor = color;
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAssemblyFullName()
        {
            if (!string.IsNullOrEmpty(typeReference.assemblyFullName))
            {
                var status = GUI.enabled;
                GUI.enabled = false;
                EditorGUILayout.LabelField(" ", "Assembly Full Name: " + typeReference.assemblyFullName);
                GUI.enabled = status;
            }
        }

        private void DrawSearch()
        {
            GUILayout.BeginVertical();
            var newName = EditorGUILayout.TextField(Label, typeReference.classFullName);
            if (newName != typeReference.classFullName)
            {
                typeReference.classFullName = newName;
                EndCheck();
                if (typeReference.ReferType == null) options = GetUniqueNames(MatchedClasses, typeReference.classFullName + ".");
            }
            options ??= GetUniqueNames(MatchedClasses, typeReference.classFullName + ".");

            GUILayout.BeginHorizontal();
            GUILayout.Space(180);
            if (GUILayout.Button(".."))
            {
                typeReference.classFullName = Backward(typeReference.classFullName, true);
                UpdateOptions();
            }

            GUILayout.EndHorizontal();
            listRect = GUILayout.BeginScrollView(listRect, GUILayout.MaxHeight(22 * Mathf.Min(8, options.Count())));

            bool updated = false;
            foreach (var item in options)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(180);
                if (GUILayout.Button(item))
                {
                    typeReference.classFullName = item;
                    updated = true;
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }
            if (updated)
            {
                UpdateOptions();
            }
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
        }

        private void UpdateOptions()
        {
            options = GetUniqueNames(MatchedClasses, typeReference.classFullName + ".");
        }

        private void DrawDropdown()
        {
            string name = typeReference.classFullName;
            string currentNamespace = name + ".";
            string broadNamespace = Backward(name);

            var names = GetUniqueNames(MatchedClasses, currentNamespace);
            var labels = names.Prepend("..").Prepend(name).ToArray();
            var result = EditorGUILayout.Popup(Label, 0, labels);
            if (result == 1)
            {
                typeReference.classFullName = broadNamespace;
            }
            else if (result > 1)
            {
                typeReference.classFullName = labels[result];
            }
        }

        private void EndCheck()
        {
            var color = GUI.contentColor;
            typeReference.classFullName = typeReference.classFullName.TrimEnd('.');
            Type type = MatchedClasses.FirstOrDefault(t => t.FullName == typeReference.classFullName);
            if (type != null)
            {
                typeReference.SetReferType(type);
                GUI.contentColor = Color.green;
                EditorGUILayout.LabelField("class: " + typeReference.classFullName.Split('.').LastOrDefault(), GUILayout.MaxWidth(150));
            }
            else
            {
                typeReference.SetReferType(null);
                GUI.contentColor = Color.red;
                EditorGUILayout.LabelField("Invalid Component", GUILayout.MaxWidth(150));
            }
            GUI.contentColor = color;
        }

        /// <summary>
        /// Backward a class
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string Backward(string name)
        {
            return (name.Contains(".") ? name[..name.LastIndexOf('.')] + "." : "");
        }

        /// <summary>
        /// Backward a class
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public string Backward(string name, bool continous)
        {
            if (continous)
            {
                do
                {
                    name = Backward(name.TrimEnd('.'));
                }
                while (GetUniqueNames(MatchedClasses, name).Count() == 1 && !string.IsNullOrEmpty(name));
            }
            else name = Backward(name);
            return name;
        }

        public static IEnumerable<string> GetUniqueNames(IEnumerable<Type> classes, string key)
        {
            HashSet<string> set = new HashSet<string>();
            if (key == ".")
            {
                key = "";
            }
            foreach (var item in classes)
            {
                if (!item.FullName.StartsWith(key)) continue;
                string[] strings = item.FullName[key.Length..].Split(".");
                set.Add($"{key}{strings[0]}{(strings.Length != 1 ? "" : string.Empty)}");
            }
            if (set.Count == 1)
            {
                //Debug.Log(key);
                string onlyKey = set.FirstOrDefault();

                //Debug.Log(onlyKey);
                var ret = GetUniqueNames(classes, onlyKey + ".");
                return ret.Count() == 0 ? set : ret;
            }
            return set;
        }

        /// <summary>
        /// Get all component type
        /// </summary>
        /// <returns></returns>
        private Type[] GetAllMatchedType()
        {
            if (allComponentClasses.TryGetValue(typeReference.BaseType, out var value))
            {
                return value;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var classes = assemblies.SelectMany(a => a.GetTypes().Where(t => t.IsVisible && !t.IsGenericType && t.IsSubclassOf(typeReference.BaseType)));
            return allComponentClasses[typeReference.BaseType] = classes.ToArray();
        }
    }
}