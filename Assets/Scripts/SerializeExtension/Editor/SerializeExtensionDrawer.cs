using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace Core
{
    [CustomPropertyDrawer(typeof(SerializeExtensionAttribute))]
    public class SerializeExtensionDrawer : PropertyDrawer
    {
        private static readonly LogicAndStack _guiEnableStack = new();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return property.isExpanded ? EditorGUI.GetPropertyHeight(property, true) : EditorGUIUtility.singleLineHeight;
        }

        private new SerializeExtensionAttribute attribute => (SerializeExtensionAttribute)base.attribute;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            label.text = attribute.NameInEditorWindow ?? label.text;
            label.tooltip = attribute.ToolTips ?? label.tooltip;

            GUI.enabled = _guiEnableStack.Push(attribute.CanWrite);
            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();

            Type fieldType = fieldInfo.FieldType;
            if (fieldType.IsAbstract || fieldType.IsInterface) {
                ShowPolymorphismField(position, property, label);
            }
            else {
                EditorGUI.PropertyField(position, property, label, true);
            }

            bool hasValueUpdated = EditorGUI.EndChangeCheck() || !attribute.CanWrite; // !attribute.CanWrite : 当变量不可写时大概率用作显示某个字段的成员信息，为了避免字段发生修改时成员信息更新不同步的问题需要保持更新才行
            if (hasValueUpdated && attribute.ProxyPropertyName != null) {
                var owner = GetFieldOwner(property);
                PropertyInfo proxy = owner.GetType().GetProperty(attribute.ProxyPropertyName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public); ;
                if (CheckPropertyProxyValid(proxy, owner, fieldType)) {
                    property.serializedObject.ApplyModifiedProperties();
                    if (proxy.CanRead && proxy.CanWrite) {
                        proxy.SetValue(owner, proxy.GetValue(owner));
                    }
                    else if (proxy.CanRead) {
                        fieldInfo.SetValue(owner, proxy.GetValue(owner));
                    }
                    else if (proxy.CanWrite) {
                        proxy.SetValue(owner, fieldInfo.GetValue(owner));
                    }
                }
            }

            GUI.enabled = _guiEnableStack.Pop();
            EditorGUI.EndProperty();
        }

        #region Show Polymorphism Field
        private readonly static Dictionary<Type, List<Type>> _subTypeDict = new();

        private void ShowPolymorphismField(Rect position, SerializedProperty property, GUIContent label) { 
            // 绘制选择框
            Type abstractType = fieldInfo.FieldType;
            List<Type> subTypes = GetSubTypes(abstractType);

            Rect labelRect = EditorGUI.IndentedRect(new(position) {
                height = EditorGUIUtility.singleLineHeight
            });
            Rect popupRect = EditorGUI.PrefixLabel(labelRect, label);

            string[] selectBoxTexts = subTypes.Select(type => type.Name).Prepend("None (null)").ToArray();
            Type fieldType = property.managedReferenceValue?.GetType();
            int currentIndex = subTypes.FindIndex(type => type == fieldType) + 1; // 选择框会占用0号位置为"None (null)" 所以下标会加一个偏移量

            GUI.enabled = attribute.CanSwitchSubType;
            int newSelectIndex = EditorGUI.Popup(popupRect, currentIndex, selectBoxTexts);
            GUI.enabled = true;

            if (newSelectIndex != currentIndex) {
                if (newSelectIndex == 0) {
                    property.managedReferenceValue = null;
                }
                else {
                    Type type = subTypes[newSelectIndex - 1]; // 将选择框的一个偏移量减回来
                    property.managedReferenceValue = CreateInstance(type);
                }
                property.serializedObject.ApplyModifiedProperties(); //更改多态类型后必须马上保存，否则后续序列化可能会出现异常
            }

            // 绘制序列化字段
            Rect foldoutRect = new(position) {
                height = EditorGUIUtility.singleLineHeight
            };
            if (property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none, true)) {
                using (new EditorGUI.IndentLevelScope()) {
                    Rect rect = position;
                    rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                    int depthLimit = property.depth + 1; // 防止循环引用，对深度做限制
                    foreach (SerializedProperty chlidProperty in property) {
                        if (chlidProperty.depth > depthLimit) continue;
                        rect.height = EditorGUI.GetPropertyHeight(chlidProperty, new GUIContent(chlidProperty.displayName, chlidProperty.tooltip), true);
                        EditorGUI.PropertyField(rect, chlidProperty, true);
                        rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
                    }
                }
            }
        }

        private List<Type> GetSubTypes(Type abstractType) {
            if (!_subTypeDict.TryGetValue(abstractType, out var subTypes)) {
                subTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(assembly => assembly.GetTypes())
                    .Where(type => abstractType.IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface)
                    .ToList();

                const int maxCacheSize = 10;
                while (_subTypeDict.Count > maxCacheSize) {
                    _subTypeDict.Remove(_subTypeDict.Keys.First());
                }
                _subTypeDict.Add(abstractType, subTypes);
            }
            return subTypes;
        }

        /// <summary>
        /// 尝试使用无参构造函数创造实例，若失败则返回未初始化类型
        /// </summary>
        private object CreateInstance(Type type) {
            foreach (var constructor in type.GetConstructors()) {
                if (constructor.GetParameters().Length == 0) {
                    return constructor.Invoke(null);
                }
            }
            return FormatterServices.GetSafeUninitializedObject(type);
        }

        #endregion

        private object GetFieldOwner(SerializedProperty property) { 
            object root = property.serializedObject.targetObject;
            string[] fieldPath = property.propertyPath.Replace("Array.data[", "[").Split('.');
            IEnumerable<string> ownerPath = fieldPath.SkipLast(1);
            foreach (string memberName in ownerPath) {
                Match match = Regex.Match(memberName, @"\[(\d+)\]"); // 匹配含有 "[整数]" 形式的字符串
                bool isArray = match.Success;
                if (isArray) {
                    int index = int.Parse(match.Groups[1].Value);
                    root = (root as IEnumerable).Cast<object>().ElementAt(index); 
                }
                else {
                    FieldInfo fieldInfo = null;
                    for (Type t = root.GetType(); fieldInfo == null && t != typeof(object); t = t.BaseType) {
                        fieldInfo = t.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                    root = fieldInfo.GetValue(root);
                }
            }
            return root;
        }

        private bool CheckPropertyProxyValid(PropertyInfo proxy, object parent, Type returnType) {
            bool isValid = true;
            if (proxy == null) {
                Debug.LogError($"{nameof(SerializeExtensionAttribute)}: In \"{parent.GetType()}\" cannot find property \"{attribute.ProxyPropertyName}\"");
                isValid = false;
            }
            else {
                if (!proxy.PropertyType.IsAssignableFrom(returnType)) {
                    Debug.LogError($"{nameof(SerializeExtensionAttribute)}: porperty \"{attribute.ProxyPropertyName}\" return type \"{proxy.PropertyType}\" is different from that of a field \"{returnType}\"");
                    isValid = false;
                }
            }
            return isValid;
        }

        /// <summary>
        /// 模拟逻辑与操作，当栈中存在至少一个False就将返回False
        /// </summary>
        private class LogicAndStack {
            private Stack<bool> _statck = new();
            private int numberOfFalseInStack;
            private bool _currentState = true;

            public bool Push(bool enable) {
                _currentState &= enable;
                _statck.Push(enable);
                numberOfFalseInStack += enable ? 0 : 1;
                return _currentState;
            }

            public bool Pop() {
                if(_statck.Count == 0) {
                    _currentState = true;
                    return _currentState;
                }

                numberOfFalseInStack -= _statck.Pop() ? 0 : 1;
                _currentState = numberOfFalseInStack == 0;
                return _currentState;
            }
        }
    }
}
