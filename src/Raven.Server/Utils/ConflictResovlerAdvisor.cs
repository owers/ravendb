﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sparrow.Json;
using Constants = Raven.Client.Constants;
namespace Raven.Server.Utils
{
    public class ConflictResovlerAdvisor
    {
        private readonly BlittableJsonReaderObject[] _docs;
        internal readonly bool IsMetadataResolver;
        private readonly JsonOperationContext _context;

        public ConflictResovlerAdvisor(IEnumerable<BlittableJsonReaderObject> docs, JsonOperationContext ctx, bool isMetadataResolver = false)
        {
            _docs = docs.ToArray();
            IsMetadataResolver = isMetadataResolver;
            _context = ctx;
        }

        public MergeResult Resolve(int indent = 1)
        {
            var result = new Dictionary<string, object>();
            for (var index = 0; index < _docs.Length; index++)
            {
                var doc = _docs[index];
                for(var indexProp = 0; indexProp<doc.Count; indexProp++)
                {
                    BlittableJsonReaderObject.PropertyDetails prop = new BlittableJsonReaderObject.PropertyDetails();
                    doc.GetPropertyByIndex(indexProp,ref prop);

                    if (result.ContainsKey(prop.Name)) // already dealt with
                        continue;

                    switch (prop.Token)
                    {
                        case BlittableJsonToken.StartObject:
                        case BlittableJsonToken.EmbeddedBlittable:
                            var objTuple = new KeyValuePair<string, BlittableJsonReaderObject>(prop.Name, (BlittableJsonReaderObject)prop.Value);
                            if (TryHandleObjectValue(index, result, objTuple) == false)
                                goto default;
                            break;
                        case BlittableJsonToken.StartArray:
                            var arrTuple = new KeyValuePair<string, BlittableJsonReaderArray>(prop.Name, (BlittableJsonReaderArray)prop.Value);
                            if (TryHandleArrayValue(index, result, arrTuple) == false)
                                goto default;
                            break;
                        default:
                            HandleSimpleValues(result, prop, index);
                            break;
                    }
                }
            }
            return GenerateOutput(result, indent);
        }

        private bool TryHandleObjectValue(int index, Dictionary<string, object> result, KeyValuePair<string, BlittableJsonReaderObject> prop)
        {
            var others = new List<BlittableJsonReaderObject>
            {
                prop.Value
            };
            for (var i = 0; i < _docs.Length; i++)
            {
                if (i == index)
                    continue;

                BlittableJsonReaderObject token;
                if (_docs[i].TryGet(prop.Key, out token))
                    return false;
                if (token == null)
                    continue;
                others.Add(token);
            }

            result.Add(prop.Key, new ConflictResovlerAdvisor(others.ToArray(), _context, prop.Key == Constants.Documents.Metadata.Key || IsMetadataResolver));
            return true;
        }

        private bool TryHandleArrayValue(int index, Dictionary<string, object> result, KeyValuePair<string, BlittableJsonReaderArray> prop)
        {
            var arrays = new List<BlittableJsonReaderArray>
            {
                prop.Value
            };

            for (var i = 0; i < _docs.Length; i++)
            {
                if (i == index)
                    continue;

                BlittableJsonReaderArray token;
                if (_docs[i].TryGet(prop.Key, out token))
                    return false;
                if (token == null)
                    continue;
                arrays.Add(token);
            }

            var set = new HashSet<Tuple<object,BlittableJsonToken>>();
            foreach (var arr in arrays)
            {
                for (var propIndex = 0; propIndex < arr.Length; propIndex++)
                {
                    var tuple = arr.GetValueTokenTupleByIndex(propIndex);
                    set.Add(tuple);
                }
            }
            BlittableJsonReaderObject reader;
            using (var mergedArray = new ManualBlittalbeJsonDocumentBuilder<UnmanagedWriteBuffer>(_context))
            {
                mergedArray.Reset(BlittableJsonDocumentBuilder.UsageMode.None);
                mergedArray.StartWriteObjectDocument();
                mergedArray.StartArrayDocument();
                foreach (var item in set)
                {
                    mergedArray.WriteValue(item.Item2,item.Item1);
                }
                mergedArray.WriteArrayEnd();
                mergedArray.FinalizeDocument();
                reader = mergedArray.CreateReader();
            }

            if (reader.Equals(prop.Value))
            {
                result.Add(prop.Key, reader);
                return true;
            }

            result.Add(prop.Key, new ArrayWithWarning(reader));
            return true;
        }


        private void HandleSimpleValues(Dictionary<string, object> result, BlittableJsonReaderObject.PropertyDetails prop, int index)
        {
            var conflicted = new Conflicted
            {
                Values = { prop }
            };
            var type = BlittableJsonReaderBase.GetTypeFromToken(prop.Token);

            for (var i = 0; i < _docs.Length; i++)
            {
                if (i == index)
                    continue;
                var other = _docs[i];
                
                BlittableJsonReaderObject.PropertyDetails otherProp = new BlittableJsonReaderObject.PropertyDetails();
                var propIndex = other.GetPropertyIndex(prop.Name);
                if (propIndex == -1)
                {
                    continue;
                }
                other.GetPropertyByIndex(propIndex, ref otherProp);
                
                if (otherProp.Token != prop.Token || (type != null && // if type is null there could not be a conflict
                    (Convert.ChangeType(prop.Value, type) != Convert.ChangeType(otherProp.Value, type))))
                {
                    conflicted.Values.Add(otherProp);
                }               
            }

            result.Add(prop.Name, conflicted.Values.Count == 1 ? prop.Value : conflicted);
        }


        private class Conflicted
        {
            public readonly HashSet<BlittableJsonReaderObject.PropertyDetails> Values = new HashSet<BlittableJsonReaderObject.PropertyDetails>();
        }

        private class ArrayWithWarning
        {
            public readonly BlittableJsonReaderObject MergedArray;

            public ArrayWithWarning(BlittableJsonReaderObject mergedArray)
            {
                MergedArray = mergedArray;
            }
        }

        public class MergeChunk
        {
            public bool IsMetadata { get; set; }
            public string Data { get; set; }
        }

        public class MergeResult
        {
            public BlittableJsonReaderObject Document { get; set; }
            public BlittableJsonReaderObject Metadata { get; set; }
        }

        private void WriteToken(ManualBlittalbeJsonDocumentBuilder<UnmanagedWriteBuffer> writer, string propertyName, Object propertyValue)
        {
            writer.WritePropertyName(propertyName);
            if (propertyValue is BlittableJsonReaderObject.PropertyDetails)
            {
                var prop = (BlittableJsonReaderObject.PropertyDetails) propertyValue;
                writer.WriteValue(prop.Token, prop.Value);
                return;
            }

            var conflicted = propertyValue as Conflicted;
            if (conflicted != null)
            {
                writer.StartWriteArray();
                writer.WriteValue(">>>> conflict start");
                foreach (BlittableJsonReaderObject.PropertyDetails item in conflicted.Values)
                {
                    writer.WriteValue(item.Token, item.Value);
                }
                writer.WriteValue("<<<< conflict end");
                writer.WriteArrayEnd();
                return;
            }

            var arrayWithWarning = propertyValue as ArrayWithWarning;
            if (arrayWithWarning != null)
            {
                writer.StartWriteArray();
                writer.WriteValue(">>>> auto merged array start");
                writer.WriteEmbeddedBlittableDocument(arrayWithWarning.MergedArray);
                writer.WriteValue("<<<< auto merged array end");
                writer.WriteArrayEnd();
                return;
            }

            throw new InvalidOperationException("Could not understand how to deal with: " + propertyValue);
        }

        private void WriteRawData(ManualBlittalbeJsonDocumentBuilder<UnmanagedWriteBuffer> writer, BlittableJsonReaderObject data, int indent)
        {
            /*var sb = new StringBuilder();
            using (var stringReader = new StringReader(data))
            {
                var first = true;
                string line;
                while ((line = stringReader.ReadLine()) != null)
                {
                    if (first == false)
                    {
                        sb.AppendLine();
                        for (var i = 0; i < indent; i++)
                        {
                            sb.Append(writer.IndentChar, writer.Indentation);
                        }
                    }

                    sb.Append(line);

                    first = false;
                }
            }
            writer.WriteRawValue(sb.ToString());*/
            writer.WriteEmbeddedBlittableDocument(data);
        }

        private void WriteConflictResolver(string name, ManualBlittalbeJsonDocumentBuilder<UnmanagedWriteBuffer> documentWriter, 
            ManualBlittalbeJsonDocumentBuilder<UnmanagedWriteBuffer> metadataWriter, ConflictResovlerAdvisor resolver, int indent)
        {
            MergeResult result = resolver.Resolve(indent);

            if (resolver.IsMetadataResolver)
            {
                if (name != "@metadata")
                    metadataWriter.WritePropertyName(name);

                WriteRawData(metadataWriter, result.Document, indent);
            }
            else
            {
                documentWriter.WritePropertyName(name);
                WriteRawData(documentWriter, result.Document, indent);
            }
        }

        private MergeResult GenerateOutput(Dictionary<string, object> result, int indent)
        {
            using (var documentWriter = new ManualBlittalbeJsonDocumentBuilder<UnmanagedWriteBuffer>(_context))
            using (var metadataWriter = new ManualBlittalbeJsonDocumentBuilder<UnmanagedWriteBuffer>(_context))
            {
                documentWriter.Reset(BlittableJsonDocumentBuilder.UsageMode.None);
                metadataWriter.Reset(BlittableJsonDocumentBuilder.UsageMode.None);
                documentWriter.StartWriteObjectDocument();
                metadataWriter.StartWriteObjectDocument();
                documentWriter.StartWriteObject();
                metadataWriter.StartWriteObject();

                foreach (var o in result)
                {
                    var resolver = o.Value as ConflictResovlerAdvisor;
                    if (resolver != null)
                    {
                        WriteConflictResolver(o.Key, documentWriter, metadataWriter, resolver, o.Key == "@metadata" ? 0 : indent + 1);
                    }
                    else
                    {
                        WriteToken(o.Key == Constants.Documents.Metadata.Key ? metadataWriter : documentWriter, o.Key, o.Value);
                    }
                }

                documentWriter.WriteObjectEnd();
                metadataWriter.WriteObjectEnd();
                documentWriter.FinalizeDocument();
                metadataWriter.FinalizeDocument();
                   
                return new MergeResult()
                {
                    Document = documentWriter.CreateReader(),
                    Metadata = metadataWriter.CreateReader()
                };
            }
        }

    }
}
