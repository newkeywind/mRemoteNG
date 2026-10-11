using System;
using System.Collections.Generic;
using System.Xml;

namespace mRemoteNG.Config.Serializers.ConnectionSerializers.Xml
{
    public sealed class XmlNodeAttributeReader
    {
        private readonly Dictionary<string, string> _attributes;

        public XmlNodeAttributeReader(XmlNode xmlNode)
        {
            if (xmlNode?.Attributes == null) return;
            _attributes = new Dictionary<string, string>(xmlNode.Attributes.Count, StringComparer.Ordinal);
            foreach (XmlAttribute attribute in xmlNode.Attributes)
            {
                _attributes[attribute.Name] = attribute.Value;
            }
        }

        public string GetAttributeAsString(string attribute, string defaultValue = "")
        {
            if (_attributes == null || !_attributes.TryGetValue(attribute, out string value))
                return defaultValue;

            return value ?? defaultValue;
        }

        public bool GetAttributeAsBool(string attribute, bool defaultValue = false)
        {
            if (_attributes == null || !_attributes.TryGetValue(attribute, out string value) || string.IsNullOrWhiteSpace(value))
                return defaultValue;

            return bool.TryParse(value, out bool valueAsBool)
                ? valueAsBool
                : defaultValue;
        }

        public int GetAttributeAsInt(string attribute, int defaultValue = 0)
        {
            if (_attributes == null || !_attributes.TryGetValue(attribute, out string value) || string.IsNullOrWhiteSpace(value))
                return defaultValue;

            return int.TryParse(value, out int valueAsInt)
                ? valueAsInt
                : defaultValue;
        }

        public T GetAttributeAsEnum<T>(string attribute, T defaultValue = default)
            where T : struct
        {
            if (_attributes == null || !_attributes.TryGetValue(attribute, out string value) || string.IsNullOrWhiteSpace(value))
                return defaultValue;

            return Enum.TryParse<T>(value, true, out T valueAsEnum)
                ? valueAsEnum
                : defaultValue;
        }
    }

    public static class XmlExtensions
    {
        public static string GetAttributeAsString(this XmlNode xmlNode, string attribute, string defaultValue = "")
        {
            string value = xmlNode?.Attributes?[attribute]?.Value;
            return value ?? defaultValue;
        }

        public static bool GetAttributeAsBool(this XmlNode xmlNode, string attribute, bool defaultValue = false)
        {
            string value = xmlNode?.Attributes?[attribute]?.Value;
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            return bool.TryParse(value, out bool valueAsBool)
                ? valueAsBool
                : defaultValue;
        }

        public static int GetAttributeAsInt(this XmlNode xmlNode, string attribute, int defaultValue = 0)
        {
            string value = xmlNode?.Attributes?[attribute]?.Value;
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            return int.TryParse(value, out int valueAsBool)
                ? valueAsBool
                : defaultValue;
        }

        public static T GetAttributeAsEnum<T>(this XmlNode xmlNode, string attribute, T defaultValue = default)
            where T : struct
        {
            string value = xmlNode?.Attributes?[attribute]?.Value;
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            return Enum.TryParse<T>(value, true, out T valueAsEnum)
                ? valueAsEnum
                : defaultValue;
        }
    }
}