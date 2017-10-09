﻿using System.Collections.Immutable;
using System.Threading.Tasks;
using AzureStorage;
using Common.Log;
using Lykke.Job.CandlesProducer.AzureRepositories.Legacy;
using Lykke.Job.CandlesProducer.Core.Domain;
using Lykke.Job.CandlesProducer.Core.Domain.Candles;

namespace Lykke.Job.CandlesProducer.AzureRepositories.Migration
{
    public class MidPriceQuoteGeneratorSnapshotMigrationRepository : ISnapshotRepository<IImmutableDictionary<string, IMarketState>>
    {
        private readonly ILog _log;
        private readonly LegacyMidPriceQuoteGeneratorSnapshotRepository _legacyRepository;
        private readonly MidPriceQuoteGeneratorSnapshotRepository _repository;

        public MidPriceQuoteGeneratorSnapshotMigrationRepository(IBlobStorage storage, ILog log)
        {
            _log = log;
            _legacyRepository = new LegacyMidPriceQuoteGeneratorSnapshotRepository(storage);
            _repository = new MidPriceQuoteGeneratorSnapshotRepository(storage);
        }

        public Task SaveAsync(IImmutableDictionary<string, IMarketState> state)
        {
            return _repository.SaveAsync(state);
        }

        public async Task<IImmutableDictionary<string, IMarketState>> TryGetAsync()
        {
            var newResult = await _repository.TryGetAsync();
            if (newResult == null)
            {
                await _log.WriteWarningAsync(nameof(CandlesGeneratorSnapshotMigrationRepository), nameof(TryGetAsync), "",
                    "Failed to get snapshot in the new format, fallback to the legacy format");

                return await _legacyRepository.TryGetAsync();
            }

            return newResult;
        }
    }
}
