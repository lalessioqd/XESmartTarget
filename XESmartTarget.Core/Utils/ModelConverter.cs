﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;

namespace XESmartTarget.Core.Utils
{
    public class ModelConverter
    {
        public IEnumerable<Type> SupportedTypes
        {
            get
            {
                List<Type> result = new List<Type>();
                Assembly currentAssembly = Assembly.GetExecutingAssembly();
                string nameSpace = "XESmartTarget.Core.";
                Type[] types = currentAssembly.GetTypes().Where(t => t != null && t.FullName.StartsWith(nameSpace) & !t.FullName.Contains("+")).ToArray();
                foreach (Type t in types)
                {
                    try
                    {
                        result.Add(t);
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
                return result;
            }
        }

        public T Deserialize<T>(string json)
        {
            return (T)Deserialize(json, typeof(T));
        }

        public object Deserialize(string json, Type type)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            JToken token = JsonConvert.DeserializeObject<JToken>(json);
            var dictionary = token.ToObject<Dictionary<string, object>>();
            return Deserialize(dictionary, type);
        }

        public object Deserialize(IDictionary<string, object> dictionary, Type type)
        {
            object instance = CreateInstance(dictionary, type);
            var props = instance.GetType().GetProperties();

            foreach (var kvp in dictionary)
            {
                string key = kvp.Key;
                var prop = props.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
                if (prop == null)
                    continue;

                object rawValue = ConvertJTokenIfNeeded(kvp.Value);
                if (rawValue == null)
                    continue;

                if (prop.Name == "OutputColumns" && prop.PropertyType == typeof(List<string>))
                {
                    var propValue = new List<string>();

                    if (rawValue is IList listVal)
                        propValue = listVal.Cast<object>().Select(x => x?.ToString()).ToList();
                    else
                        propValue = new List<string> { rawValue.ToString() };

                    prop.SetValue(instance, propValue);
                    continue;
                }
                else if (prop.Name == "OutputMeasurement" && prop.PropertyType == typeof(string))
                {
                    prop.SetValue(instance, rawValue.ToString());
                    continue;
                }
                else if (prop.Name == "ServerName")
                {
                    if (prop.PropertyType == typeof(string))
                    {
                        prop.SetValue(instance, rawValue.ToString());
                    }
                    else
                    {
                        prop.SetValue(instance, ConvertToStringArray(rawValue));
                    }
                    continue;
                }
                else if (prop.Name == "Target")
                {
                    if (rawValue is IList listVal)
                    {
                        var deserializedList = new List<Target>();
                        foreach (var el in listVal)
                        {
                            if (ConvertJTokenIfNeeded(el) is IDictionary<string, object> dic)
                            {
                                var targetObj = Deserialize(dic, typeof(Target));
                                deserializedList.Add((Target)targetObj);
                            }
                        }
                        prop.SetValue(instance, deserializedList.ToArray());
                    }
                    else if (rawValue is IDictionary<string, object> singleDict)
                    {
                        var targetObj = Deserialize(singleDict, typeof(Target));
                        prop.SetValue(instance, new Target[] { (Target)targetObj });
                    }
                    continue;
                }

                if (rawValue is IDictionary<string, object> subDict)
                {
                    prop.SetValue(instance, Deserialize(subDict, prop.PropertyType));
                }
                else if (rawValue is IList list && prop.PropertyType.IsGenericType)
                {
                    var listInstance = (IList)Activator.CreateInstance(prop.PropertyType);
                    Type elementType = prop.PropertyType.GetGenericArguments()[0];
                    foreach (var item in list)
                    {
                        object convertedItem = ConvertJTokenIfNeeded(item);
                        if (convertedItem is IDictionary<string, object> dictItem)
                        {
                            convertedItem = Deserialize(dictItem, elementType);
                        }
                        listInstance.Add(convertedItem);
                    }
                    prop.SetValue(instance, listInstance);
                }
                else if (prop.PropertyType.IsEnum)
                {
                    prop.SetValue(instance, Enum.Parse(prop.PropertyType, rawValue.ToString()));
                }
                else
                {
                    prop.SetValue(instance, GetValueOfType(rawValue, prop.PropertyType));
                }
            }
            return instance;
        }

        private object CreateInstance(IDictionary<string, object> dictionary, Type type)
        {
            try
            {
                if (type.IsAbstract && dictionary.TryGetValue("__type", out var subTypeObj) && subTypeObj != null)
                {
                    var subTypeName = subTypeObj.ToString();
                    string fullTypeName = subTypeName.Contains(".")
                        ? subTypeName
                        : $"XESmartTarget.Core.Responses.{subTypeName}";
                    var realType = Assembly.GetExecutingAssembly().GetType(fullTypeName) ?? Type.GetType(fullTypeName);
                    if (realType != null && !realType.IsAbstract)
                        return Activator.CreateInstance(realType);
                }
                return Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception during instance creation: " + ex.Message);
                return FormatterServices.GetUninitializedObject(type);
            }
        }

        private object ConvertJTokenIfNeeded(object value)
        {
            if (value is JObject jObj)
            {
                var dict = new Dictionary<string, object>();
                foreach (var prop in jObj.Properties())
                {
                    dict[prop.Name] = ConvertJTokenIfNeeded(prop.Value);
                }
                return dict;
            }
            else if (value is JArray jArr)
            {
                var list = new List<object>();
                foreach (var item in jArr)
                {
                    list.Add(ConvertJTokenIfNeeded(item));
                }
                return list;
            }
            else if (value is JValue jVal)
            {
                return jVal.Value;
            }
            return value;
        }

        private string[] ConvertToStringArray(object val)
        {
            if (val == null)
                return null;
            if (val is IEnumerable enumerable && !(val is string))
            {
                var strList = new List<string>();
                foreach (var item in enumerable)
                {
                    strList.Add(item?.ToString());
                }
                return strList.ToArray();
            }
            return new string[] { val.ToString() };
        }

        private object GetValueOfType(object v, Type propertyType)
        {
            if (propertyType == typeof(string))
                return v?.ToString();
            else if (propertyType == typeof(bool))
                return Convert.ToBoolean(v);
            else if (propertyType == typeof(int))
                return Convert.ToInt32(v);
            else if (propertyType == typeof(long))
                return Convert.ToInt64(v);
            else
                return v;
        }

        public IDictionary<string, object> Serialize(object obj)
        {
            throw new NotImplementedException();
        }
    }
}
