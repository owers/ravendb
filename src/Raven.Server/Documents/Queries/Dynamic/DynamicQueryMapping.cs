﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using Raven.Abstractions.Data;
using Raven.Server.Documents.Indexes.Auto;
using Raven.Server.Documents.Queries.Parse;
using Raven.Server.Documents.Queries.Sort;

namespace Raven.Server.Documents.Queries.Dynamic
{
    public class DynamicQueryMapping
    {
        public string ForCollection { get; private set; }

        public DynamicSortInfo[] SortDescriptors { get; private set; } = new DynamicSortInfo[0];

        public DynamicQueryMappingItem[] MapFields { get; private set; } = new DynamicQueryMappingItem[0];

        public string[] HighlightedFields { get; private set; }

        private DynamicQueryMapping()
        {
        }

        public AutoIndexDefinition CreateAutoIndexDefinition()
        {
            return new AutoIndexDefinition(ForCollection, MapFields.Select(field =>
                new AutoIndexField(name: field.From,
                    sortOption: SortDescriptors.FirstOrDefault(x => field.To.Equals(x.Field))?.FieldType,
                    highlighted: HighlightedFields.Any(x => field.To.Equals(x)))).ToArray());
        }

        public static DynamicQueryMapping Create(string entityName, IndexQuery query)
        {
            var fields = SimpleQueryParser.GetFieldsForDynamicQuery(query); // TODO arek - not sure if we really need a Tuple<string, string> here

            if (query.SortedFields != null)
            {
                foreach (var sortedField in query.SortedFields)
                {
                    var field = sortedField.Field;

                    if (field == Constants.TemporaryScoreValue)
                        continue;

                    if (field.StartsWith(Constants.AlphaNumericFieldName) ||
                        field.StartsWith(Constants.RandomFieldName) ||
                        field.StartsWith(Constants.CustomSortFieldName))
                    {
                        field = SortFieldHelper.CustomField(field).Name;
                    }

                    if (field.EndsWith("_Range"))
                        field = field.Substring(0, field.Length - "_Range".Length);

                    fields.Add(Tuple.Create(SimpleQueryParser.TranslateField(field), field));
                }
            }

            var dynamicQueryMapping = new DynamicQueryMapping
            {
                ForCollection = entityName,
                HighlightedFields = query.HighlightedFields.EmptyIfNull().Select(x => x.Field).ToArray(),
                SortDescriptors = GetSortInfo(fieldName =>
                {
                    if (fields.Any(x => x.Item2 == fieldName || x.Item2 == (fieldName + "_Range")) == false)
                        fields.Add(Tuple.Create(fieldName, fieldName));
                }, query)
            };

            dynamicQueryMapping.SetupFieldsToIndex(fields);
            dynamicQueryMapping.SetupSortDescriptors(dynamicQueryMapping.SortDescriptors);

            return dynamicQueryMapping;
        }

        private void SetupSortDescriptors(DynamicSortInfo[] sortDescriptors)
        {
            foreach (var dynamicSortInfo in sortDescriptors)
            {
                dynamicSortInfo.Field = ReplaceInvalidCharactersForFields(dynamicSortInfo.Field);
            }
        }
       
        static readonly Regex replaceInvalidCharacterForFields = new Regex(@"[^\w_]", RegexOptions.Compiled); // TODO arek - we should be able to get rid of it - we already have it in AutoIndexDefinition

        private void SetupFieldsToIndex(IEnumerable<Tuple<string, string>> fields)
        {
            MapFields = fields.Select(x => new DynamicQueryMappingItem
            {
                From = x.Item1,
                To = ReplaceInvalidCharactersForFields(x.Item2),
                QueryFrom = EscapeParentheses(x.Item2)
            }).OrderByDescending(x => x.QueryFrom.Length).ToArray();

        }

        private string EscapeParentheses(string str)
        {
            return str.Replace("(", @"\(").Replace(")", @"\)");
        }

        public static string ReplaceInvalidCharactersForFields(string field)
        {
            return replaceInvalidCharacterForFields.Replace(field, "_");
        }

        public static DynamicSortInfo[] GetSortInfo(Action<string> addField, IndexQuery indexQuery)
        {
            var sortInfo = new List<DynamicSortInfo>();
            if (indexQuery.SortHints == null)
                return new DynamicSortInfo[0];

            foreach (var sortOptions in indexQuery.SortHints)
            {
                var key = sortOptions.Key;
                var fieldName =
                    key.EndsWith("_Range") ?
                          key.Substring("SortHint-".Length, key.Length - "SortHint-".Length - "_Range".Length)
                        : key.Substring("SortHint-".Length);
                sortInfo.Add(new DynamicSortInfo
                {
                    Field = fieldName,
                    FieldType = sortOptions.Value
                });
            }

            return sortInfo.ToArray();
        }
    }
}