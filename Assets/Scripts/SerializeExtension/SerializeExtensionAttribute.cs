using System;
using UnityEngine;

namespace Core
{
    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeExtensionAttribute : PropertyAttribute {
        private string? _nameInEditorWindow;
        private string? _toolTips;
        private string? _proxyPropertyName;
        private bool _canWrite;
        private bool _canSwitchSubType;

        public string? NameInEditorWindow => _nameInEditorWindow;

        public string? ProxyPropertyName => _proxyPropertyName;

        public bool CanWrite => _canWrite;

        public string? ToolTips => _toolTips;

        public bool CanSwitchSubType => _canSwitchSubType;

        public SerializeExtensionAttribute(string? nameInEditorWindow = null, string? toolTips = null, int order = 0, bool canWrite = true, string? proxyPropertyName = null, bool canSwitchSubType = true) {
            base.order = order;
            _canWrite = canWrite;
            _proxyPropertyName = proxyPropertyName;
            _nameInEditorWindow = nameInEditorWindow;
            _toolTips = toolTips;
            _canSwitchSubType = canSwitchSubType;
        }
    }
}
