﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Raven.Abstractions.Data;
using Raven.Client.Data.Indexes;
using Raven.Server.Config.Categories;
using Raven.Server.Documents.Indexes.MapReduce;
using Raven.Server.Documents.Indexes.Persistence.Lucene;
using Raven.Server.ServerWide.Context;
using Sparrow.Logging;
using Sparrow.Utils;

namespace Raven.Server.Documents.Indexes.Workers
{
    public class MapDocuments : IIndexingWork
    {
        protected Logger _logger;

        private readonly Index _index;
        private readonly IndexingConfiguration _configuration;
        private readonly MapReduceIndexingContext _mapReduceContext;
        private readonly DocumentsStorage _documentsStorage;
        private readonly IndexStorage _indexStorage;

        public MapDocuments(Index index, DocumentsStorage documentsStorage, IndexStorage indexStorage, 
                            IndexingConfiguration configuration, MapReduceIndexingContext mapReduceContext)
        {
            _index = index;
            _configuration = configuration;
            _mapReduceContext = mapReduceContext;
            _documentsStorage = documentsStorage;
            _indexStorage = indexStorage;
            _logger = LoggingSource.Instance
                .GetLogger<MapDocuments>(indexStorage.DocumentDatabase.Name);
        }

        public string Name => "Map";

        public const long MaximumAmountOfMemoryToUsePerIndex = 1024 * 1024 * 1024L; // TODO: read from configuration value

        public bool Execute(DocumentsOperationContext databaseContext, TransactionOperationContext indexContext,
            Lazy<IndexWriteOperation> writeOperation, IndexingStatsScope stats, CancellationToken token)
        {
            var threadAllocations = NativeMemory.ThreadAllocations.Value;
            var moreWorkFound = false;
            foreach (var collection in _index.Collections)
            {
                using (var collectionStats = stats.For("Collection_" + collection))
                {
                    if (_logger.IsInfoEnabled)
                        _logger.Info($"Executing map for '{_index.Name} ({_index.IndexId})'. Collection: {collection}.");

                    var lastMappedEtag = _indexStorage.ReadLastIndexedEtag(indexContext.Transaction, collection);

                    if (_logger.IsInfoEnabled)
                        _logger.Info($"Executing map for '{_index.Name} ({_index.IndexId})'. LastMappedEtag: {lastMappedEtag}.");

                    var lastEtag = lastMappedEtag;
                    var count = 0;
                    var resultsCount = 0;

                    var sw = Stopwatch.StartNew();
                    IndexWriteOperation indexWriter = null;

                    using (databaseContext.OpenReadTransaction())
                    {
                        IEnumerable<Document> documents;

                        if (collection == Constants.Indexing.AllDocumentsCollection)
                            documents = _documentsStorage.GetDocumentsAfter(databaseContext, lastEtag + 1, 0, int.MaxValue);
                        else
                            documents = _documentsStorage.GetDocumentsAfter(databaseContext, collection, lastEtag + 1, 0, int.MaxValue);

                        using (var docsEnumerator = _index.GetMapEnumerator(documents, collection, indexContext))
                        {
                            IEnumerable mapResults;

                            while (docsEnumerator.MoveNext(out mapResults))
                            {
                                token.ThrowIfCancellationRequested();

                                if (indexWriter == null)
                                    indexWriter = writeOperation.Value;

                                var current = docsEnumerator.Current;

                                if (_logger.IsInfoEnabled)
                                    _logger.Info($"Executing map for '{_index.Name} ({_index.IndexId})'. Processing document: {current.Key}.");

                                collectionStats.RecordMapAttempt();

                                count++;
                                lastEtag = current.Etag;

                                try
                                {
                                    resultsCount  += 
                                        _index.HandleMap(current.LoweredKey, mapResults, indexWriter, indexContext, collectionStats);

                                    collectionStats.RecordMapSuccess();
                                }
                                catch (Exception e)
                                {
                                    collectionStats.RecordMapError();
                                    if (_logger.IsInfoEnabled)
                                        _logger.Info($"Failed to execute mapping function on '{current.Key}' for '{_index.Name} ({_index.IndexId})'.",e);

                                    collectionStats.AddMapError(current.Key,
                                        $"Failed to execute mapping function on {current.Key}. Exception: {e}");
                                }

                                if (threadAllocations.Allocations > MaximumAmountOfMemoryToUsePerIndex)
                                    break;
                            }
                        }
                    }

                    if (count == 0)
                        continue;

                    if (_logger.IsInfoEnabled)
                        _logger.Info($"Executing map for '{_index.Name} ({_index.IndexId})'. Processed {count:#,#;;0} documents and {resultsCount:#,#;;0} map results in '{collection}' collection in {sw.ElapsedMilliseconds:#,#;;0} ms.");

                    if (_index.Type.IsMap())
                    {
                        _indexStorage.WriteLastIndexedEtag(indexContext.Transaction, collection, lastEtag);
                    }
                    else
                    {
                        _mapReduceContext.ProcessedDocEtags[collection] = lastEtag;
                    }

                    moreWorkFound = true;
                }
            }

            return moreWorkFound;
        }
    }
}