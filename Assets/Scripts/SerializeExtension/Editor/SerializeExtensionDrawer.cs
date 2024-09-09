using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections;

namespace Core
{
    [CustomPropertyDrawer(typeof(SerializeExtensionAttribute))]
    public class SerializeExtensionDrawer : PropertyDrawer
    {
        private static LogicAndStack _guiEnableStack = new();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
            return property.isExpanded ? EditorGUI.GetPropertyHeight(property, true) : EditorGUIUtility.singleLineHeight;
        }

        private new SerializeExtensionAttribute attribute => (SerializeExtensionAttribute)base.attribute;


        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
            if(attribute.NameInEditorWindow != null) {
                label.text = attribute.NameInEditorWindow;
            }   
            if(attribute.ToolTips != null) {
                label.tooltip = attribute.ToolTips;
            }

            Type fieldType = null;
            bool isUnityObject = typeof(UnityEngine.Object).IsAssignableFrom(fieldInfo.FieldType);
            if (isUnityObject || fieldInfo.FieldType.IsValueType || property.propertyType != SerializedPropertyType.ManagedReference) {
                fieldType = fieldInfo.FieldType;
            }
            else {
                string[] info = property.managedReferenceFieldTypename.Split();
                string asseblyName = info[0], typeName = info[1];
                Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(item => item.GetName().Name == asseblyName);
                fieldType = assembly.GetType(typeName);
            }

            var owner = GetFieldOwner(property);
            PropertyInfo proxy = null;
            bool proxyIsValid = false;
            if (attribute.ProxyPropertyName != null && (fieldType.IsValueType || isUnityObject)) {
                proxy = owner.GetType().GetProperty(attribute.ProxyPropertyName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                proxyIsValid = CheckPropertyProxyValid(proxy, owner, fieldType);
            }

            GUI.enabled = _guiEnableStack.Push(attribute.CanWrite);
            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            if (fieldType.IsAbstract || fieldType.IsInterface) {
                ShowPolymorphismField(fieldType, position, property, label);
            }
            else {
                EditorGUI.PropertyField(position, property, label, true);
            }

            bool hasValueUpdated = EditorGUI.EndChangeCheck() || !attribute.CanWrite; // !attribute.CanWrite : 当变量不可写时大概率用作显示某个字段的成员信息，为了避免字段发生修改时成员信息更新不同步的问题需要保持更新才行
            if (proxyIsValid && hasValueUpdated) {  
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
            GUI.enabled = _guiEnableStack.Pop();
            EditorGUI.EndProperty();
        }

        #region Show Polymorphism Field
        private readonly static Dictionary<Type, List<Type>> _subTypeDict = new();

        private void ShowPolymorphismField(Type abstractType, Rect position, SerializedProperty property, GUIContent label) {
            Rect labelRect = EditorGUI.IndentedRect(new(position) {
                height = EditorGUIUtility.singleLineHeight
            });
            Rect popupRect = EditorGUI.PrefixLabel(labelRect, label);
            if (attribute.CanSwitchSubType) {
                var subTypes = GetSubTypes(abstractType);

                var fieldType = property.managedReferenceValue?.GetType();
                int currentIndex = subTypes.FindIndex(type => type == fieldType) + 1; // 会占用选择框0号位置为"None (null)" 所以下标会加一个偏移量
                string[] selectBoxText = subTypes.Select(type => type.Name).Prepend("None (null)").ToArray();

                int newSelectIndex = EditorGUI.Popup(popupRect, currentIndex, selectBoxText);
                if (newSelectIndex != currentIndex) {
                    if (newSelectIndex == 0) {
                        property.managedReferenceValue = null;
                    }
                    else {
                        bool hasDefaultConstructor = false;

                        Type type = subTypes[newSelectIndex - 1]; // 将选择框的一个偏移量减回来
                        ConstructorInfo[] constructors = type.GetConstructors();
                        // 查找无参构造函数
                        foreach (var constructor in constructors) {
                            if (constructor.GetParameters().Length == 0) {
                                // 调用无参构造函数并返回实例
                                hasDefaultConstructor = true;
                               property.managedReferenceValue = constructor.Invoke(null);
                            }
                        }

                        if (!hasDefaultConstructor) {
                            property.managedReferenceValue = FormatterServices.GetSafeUninitializedObject(type);
                        }
                    }
                    property.serializedObject.ApplyModifiedProperties(); //更改多态类型后必须马上保存，否则后续序列化可能会出现异常
                }
            }

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

            static List<Type> GetSubTypes(Type abstractType) {
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

            //void SetPropertymanagedReferenceValue(List<Type> subTypes, int selectIndex) {
            //    if (selectIndex == curIndex || selectIndex < 0 ||  selectIndex >= subTypes.Count) return;
            //    property.managedReferenceValue = FormatterServices.GetSafeUninitializedObject(subTypes[selectIndex]);
            //    curIndex = selectIndex;
            //    property.serializedObject.ApplyModifiedProperties(); //更改多态类型后必须马上保存，否则后续序列化可能会出现异常
            //}
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
