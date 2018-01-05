using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

using Raven.Abstractions;
using Raven.Abstractions.Json;
using Raven.Abstractions.Json.Linq;
using Raven.Imports.Newtonsoft.Json.Linq;
using Raven.Imports.Newtonsoft.Json.Utilities;
using Raven.Abstractions.Data;
using System.Text;
using System.Reflection;

namespace Raven.Json.Linq
{
    public static class Extensions
    {
        /// <summary>
        /// Converts the value.
        /// </summary>
        /// <typeparam name="U">The type to convert the value to.</typeparam>
        /// <param name="value">A <see cref="RavenJToken"/> cast as a <see cref="IEnumerable{T}"/> of <see cref="RavenJToken"/>.</param>
        /// <returns>A converted value.</returns>
        public static U Value<U>(this IEnumerable<RavenJToken> value)
        {
            return value.Value<RavenJToken, U>();
        }

        /// <summary>
        /// Converts the value.
        /// </summary>
        /// <typeparam name="T">The source collection type.</typeparam>
        /// <typeparam name="U">The type to convert the value to.</typeparam>
        /// <param name="value">A <see cref="RavenJToken"/> cast as a <see cref="IEnumerable{T}"/> of <see cref="RavenJToken"/>.</param>
        /// <returns>A converted value.</returns>
        public static U Value<T, U>(this IEnumerable<T> value) where T : RavenJToken
        {
            var token = value as RavenJToken;
            if (token == null)
                throw new ArgumentException("Source value must be a RavenJToken.");

            return token.Convert<U>();
        }

        /// <summary>
        /// Returns a collection of converted child values of every object in the source collection.
        /// </summary>
        /// <typeparam name="U">The type to convert the values to.</typeparam>
        /// <param name="source">An <see cref="IEnumerable{T}"/> of <see cref="RavenJToken"/> that contains the source collection.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> that contains the converted values of every node in the source collection.</returns>
        public static IEnumerable<U> Values<U>(this IEnumerable<RavenJToken> source)
        {
            return Values<U>(source, null);
        }

        /// <summary>
        /// Returns a collection of child values of every object in the source collection with the given key.
        /// </summary>
        /// <param name="source">An <see cref="IEnumerable{T}"/> of <see cref="RavenJToken"/> that contains the source collection.</param>
        /// <param name="key">The token key.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="RavenJToken"/> that contains the values of every node in the source collection with the given key.</returns>
        public static IEnumerable<RavenJToken> Values(this IEnumerable<RavenJToken> source, string key)
        {
            return Values<RavenJToken>(source, key);
        }

        /// <summary>
        /// Returns a collection of child values of every object in the source collection.
        /// </summary>
        /// <param name="source">An <see cref="IEnumerable{T}"/> of <see cref="RavenJToken"/> that contains the source collection.</param>
        /// <returns>An <see cref="IEnumerable{T}"/> of <see cref="RavenJToken"/> that contains the values of every node in the source collection.</returns>
        public static IEnumerable<RavenJToken> Values(this IEnumerable<RavenJToken> source)
        {
            return source.Values(null);
        }

        internal static IEnumerable<U> Values<U>(this IEnumerable<RavenJToken> source, string key)
        {
            foreach (RavenJToken token in source)
            {
                if (token is RavenJValue)
                {
                    yield return Convert<U>(token);
                }
                else
                {
                    foreach (var t in token.Values<U>())
                    {
                        yield return t;
                    }
                }

                var ravenJObject = (RavenJObject)token;

                RavenJToken value = ravenJObject[key];
                if (value != null)
                    yield return value.Convert<U>();
            }

            yield break;
        }

        internal static U Convert<U>(this RavenJToken token)
        {
            if (token is RavenJArray && typeof(U) == typeof(RavenJObject))
            {
                var ar = (RavenJArray)token;
                var o = new RavenJObject();
                foreach (RavenJObject item in ar)
                {
                    o[item["Key"].Value<string>()] = item["Value"];
                }
                return (U)(object)o;
            }

            bool cast = typeof(RavenJToken).IsAssignableFrom(typeof(U));

            return Convert<U>(token, cast);
        }

        internal static IEnumerable<U> Convert<U>(this IEnumerable<RavenJToken> source)
        {
            bool cast = typeof(RavenJToken).IsAssignableFrom(typeof(U));

            return source.Select(token => Convert<U>(token, cast));
        }

        internal static U Convert<U>(this RavenJToken token, bool cast)
        {
            if (cast)
            {
                // HACK
                return (U)(object)token;
            }
            if (token == null || token.Type == JTokenType.Null)
                return default(U);

            var value = token as RavenJValue;
            if (value == null)
                throw new InvalidCastException("Cannot cast {0} to {1}.".FormatWith(CultureInfo.InvariantCulture, token.GetType(), typeof(U)));

            if (value.Value is U)
                return (U)value.Value;

            Type targetType = typeof(U);

            if (targetType.IsGenericType() && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (value.Value == null)
                    return default(U);

                targetType = Nullable.GetUnderlyingType(targetType);
            }
            if (targetType == typeof(Guid))
            {
                if (value.Value == null)
                    return default(U);
                return (U)(object)new Guid(value.Value.ToString());
            }
            if (targetType == typeof(string))
            {
                if (value.Value == null)
                    return default(U);
                return (U)(object)value.Value.ToString();
            }
            if (targetType == typeof(DateTime))
            {
                var s = value.Value as string;
                if (s != null)
                {
                    DateTime dateTime;
                    if (DateTime.TryParseExact(s, Default.DateTimeFormatsToRead, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out dateTime))
                        return (U)(object)dateTime;

                    dateTime = RavenJsonTextReader.ParseDateMicrosoft(s);
                    return (U)(object)dateTime;
                }
                if (value.Value is DateTimeOffset)
                {
                    return (U)(object)((DateTimeOffset)value.Value).UtcDateTime;
                }
            }
            if (targetType == typeof(DateTimeOffset))
            {
                var s = value.Value as string;
                if (s != null)
                {
                    DateTimeOffset dateTimeOffset;
                    if (DateTimeOffset.TryParseExact(s, Default.DateTimeFormatsToRead, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out dateTimeOffset))
                        return (U)(object)dateTimeOffset;

                    return default(U);
                }
                if (value.Value is DateTime)
                {
                    return (U)(object)(new DateTimeOffset((DateTime)value.Value));
                }
            }
            if (targetType == typeof(byte[]) && value.Value is string)
            {
                return (U)(object)System.Convert.FromBase64String((string)value.Value);
            }

            if (value.Value == null && typeof(U).IsValueType())
                throw new InvalidOperationException("value.Value == null and conversion target type is not nullable");

            try
            {
                return (U)System.Convert.ChangeType(value.Value, targetType, CultureInfo.InvariantCulture);
            }
            catch (Exception e)
            {
                if (value.Value != null)
                    throw new InvalidOperationException(string.Format("Unable to find suitable conversion for {0} since it is not predefined and does not implement IConvertible. ", value.Value.GetType()), e);

                throw new InvalidOperationException(string.Format("Unable to find suitable conversion for {0} since it is not predefined ", value), e);
            }
        }

        public static bool CompareRavenJArrayData(this ICollection<DocumentsChanges> docChanges, RavenJArray selfArray, RavenJArray otherArray, string fieldArrName)
        {
            IEnumerable<RavenJToken> differences = selfArray.Length < otherArray.Length ? otherArray : selfArray;

            if (!differences.Any())
                return true;

            int index = 0;
            foreach (var dif in differences)
            {
                var changes = new DocumentsChanges
                {
                    FieldName = string.Format("{0}[{1}]", fieldArrName, index)
                };

                if (selfArray.Length < otherArray.Length)
                {
                    if (index < selfArray.Length)
                    {
                        if (!selfArray[index].DeepEquals(otherArray[index], (List<DocumentsChanges>)docChanges))
                        {
                            List<DocumentsChanges> docChangesList = docChanges.ToList();
                            docChangesList[docChangesList.Count - 1].FieldName = selfArray[index].Type == JTokenType.Object ?
                                String.Format("{0}.{1}", changes.FieldName, docChangesList[docChangesList.Count - 1].FieldName) :
                                 String.Format("{0}", changes.FieldName);
                        }
                    }
                    else
                    {
                        changes.Change = DocumentsChanges.ChangeType.ArrayValueRemoved;
                        changes.FieldOldValue = dif.ToString();
                        changes.FieldOldType = dif.Type.ToString();
                        docChanges.Add(changes);
                    }

                }

                if (selfArray.Length > otherArray.Length)
                {
                    if (index < otherArray.Length)
                    {
                        if (!selfArray[index].DeepEquals(otherArray[index], (List<DocumentsChanges>)docChanges))
                        {
                            List<DocumentsChanges> docChangesList = docChanges.ToList();

                            docChangesList[docChangesList.Count - 1].FieldName = otherArray[index].Type == JTokenType.Object ?
                                String.Format("{0}.{1}", changes.FieldName, docChangesList[docChangesList.Count - 1].FieldName) :
                                 String.Format("{0}", changes.FieldName);
                        }
                    }
                    else
                    {
                        changes.Change = DocumentsChanges.ChangeType.ArrayValueAdded;
                        changes.FieldNewValue = dif.ToString();
                        changes.FieldNewType = dif.Type.ToString();
                        docChanges.Add(changes);
                    }


                }
                index++;
            }
            return false;
        }

        public static bool CompareDifferentLengthRavenJObjectData(this ICollection<DocumentsChanges> docChanges, RavenJObject otherObj, RavenJObject selfObj, string fieldName)
        {

            var diffData = new Dictionary<string, string>();
            RavenJToken token;
            if (otherObj.Count == 0)
            {
                foreach (var kvp in selfObj.Properties)
                {
                    var changes = new DocumentsChanges();

                    if (selfObj.Properties.TryGetValue(kvp.Key, out token))
                    {
                        changes.FieldNewValue = token.ToString();
                        changes.FieldNewType = token.Type.ToString();
                        changes.Change = DocumentsChanges.ChangeType.NewField;

                        changes.FieldName = kvp.Key;
                    }

                    changes.FieldOldValue = "null";
                    changes.FieldOldType = "null";

                    docChanges.Add(changes);
                }

                return false;
            }
            FillDifferentJsonData(selfObj.Properties, otherObj.Properties, diffData);

            foreach (var key in diffData.Keys)
            {
                var changes = new DocumentsChanges
                {
                    FieldOldType = otherObj.Type.ToString(),
                    FieldNewType = selfObj.Type.ToString(),
                    FieldName = key
                };

                if (selfObj.Count < otherObj.Count)
                {
                    changes.Change = DocumentsChanges.ChangeType.RemovedField;

                    changes.FieldOldValue = diffData[key];
                }
                else
                {
                    changes.Change = DocumentsChanges.ChangeType.NewField;
                    changes.FieldNewValue = diffData[key];
                }
                docChanges.Add(changes);
            }
            return false;
        }


        private static void FillDifferentJsonData(DictionaryWithParentSnapshot selfObj, DictionaryWithParentSnapshot otherObj, Dictionary<string, string> diffData)
        {
            Debug.Assert(diffData != null, "Precaution --> parameter should not be null");

            string[] diffNames;
            DictionaryWithParentSnapshot bigObj;

            if (selfObj.Keys.Count < otherObj.Keys.Count)
            {
                diffNames = otherObj.Keys.Except(selfObj.Keys).ToArray();
                bigObj = otherObj;
            }
            else
            {
                diffNames = selfObj.Keys.Except(otherObj.Keys).ToArray();
                bigObj = selfObj;
            }
            foreach (var kvp in diffNames)
            {
                RavenJToken token;
                if (bigObj.TryGetValue(kvp, out token))
                {
                    diffData[kvp] = token.ToString();
                }
            }
        }
        public static void AddChanges(this List<DocumentsChanges> docChanges, DocumentsChanges.ChangeType change)
        {
            docChanges.Add(new DocumentsChanges
            {
                Change = change
            });

        }
        public static void AddChanges(this ICollection<DocumentsChanges> docChanges, KeyValuePair<string, RavenJToken> kvp, RavenJToken token, string fieldName)
        {
            var changes = new DocumentsChanges
            {
                FieldNewType = kvp.Value.Type.ToString(),
                FieldOldType = token?.Type.ToString(),
                FieldNewValue = kvp.Value.ToString(),
                FieldOldValue = token?.ToString(),
                Change = DocumentsChanges.ChangeType.FieldChanged,
                FieldName = fieldName
            };
            docChanges.Add(changes);

        }
        public static void AddChanges(this ICollection<DocumentsChanges> docChanges, RavenJToken curThisReader, RavenJToken curOtherReader, string fieldName)
        {
            var changes = new DocumentsChanges
            {
                FieldNewType = curThisReader.Type.ToString(),
                FieldOldType = curOtherReader.Type.ToString(),
                FieldNewValue = curThisReader.ToString(),
                FieldOldValue = curOtherReader.ToString(),
                Change = DocumentsChanges.ChangeType.FieldChanged,
                FieldName = fieldName
            };
            docChanges.Add(changes);

        }
    }
}
