﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using Lykke.Domain.Prices.Model;
using Lykke.Job.CandlesProducer.Core;
using Lykke.Job.CandlesProducer.Core.Services.Candles;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Subscriber;

namespace Lykke.Job.CandlesProducer.Services.Candles
{
    public class MtQuotesSubscriber : IQuotesSubscriber
    {
        private readonly ILog _log;
        private readonly ICandlesManager _candlesManager;
        private readonly AppSettings.RabbitSettingsWithDeadLetter _rabbitSettings;

        private RabbitMqSubscriber<MtQuote> _subscriber;

        public MtQuotesSubscriber(ILog log, ICandlesManager candlesManager, AppSettings.RabbitSettingsWithDeadLetter rabbitSettings)
        {
            _log = log;
            _candlesManager = candlesManager;
            _rabbitSettings = rabbitSettings;
        }

        public void Start()
        {
            var settings = new RabbitMqSubscriptionSettings
            {
                ConnectionString = _rabbitSettings.ConnectionString,
                QueueName = $"{_rabbitSettings.ExchangeName}.candlesproducer",
                ExchangeName = _rabbitSettings.ExchangeName,
                DeadLetterExchangeName = _rabbitSettings.DeadLetterExchangeName,
                RoutingKey = "",
                IsDurable = true
            };

            try
            {
                _subscriber = new RabbitMqSubscriber<MtQuote>(settings, 
                    new ResilientErrorHandlingStrategy(_log, settings, 
                        retryTimeout: TimeSpan.FromSeconds(10),
                        next: new DeadQueueErrorHandlingStrategy(_log, settings)))
                    .SetMessageDeserializer(new JsonMessageDeserializer<MtQuote>())
                    .SetMessageReadStrategy(new MessageReadQueueStrategy())
                    .Subscribe(ProcessQuoteAsync)
                    .CreateDefaultBinding()
                    .SetLogger(_log)
                    .Start();
            }
            catch (Exception ex)
            {
                _log.WriteErrorAsync(nameof(QuotesSubscriber), nameof(Start), null, ex).Wait();
                throw;
            }
        }

        public void Stop()
        {
            _subscriber.Stop();
        }

        private async Task ProcessQuoteAsync(MtQuote quote)
        {
            try
            {
                var validationErrors = ValidateQuote(quote);
                if (validationErrors.Any())
                {
                    var message = string.Join("\r\n", validationErrors);
                    await _log.WriteWarningAsync(nameof(MtQuotesSubscriber), nameof(ProcessQuoteAsync), quote.ToJson(), message);

                    return;
                }

                var bidQuote = new Quote
                {
                    AssetPair = quote.Instrument,
                    IsBuy = true,
                    Price = quote.Bid,
                    Timestamp = quote.Date
                };

                var askQuote = new Quote
                {
                    AssetPair = quote.Instrument,
                    IsBuy = false,
                    Price = quote.Ask,
                    Timestamp = quote.Date
                };

                await _candlesManager.ProcessQuoteAsync(bidQuote);
                await _candlesManager.ProcessQuoteAsync(askQuote);
            }
            catch (Exception ex)
            {
                await _log.WriteErrorAsync(nameof(MtQuotesSubscriber), nameof(ProcessQuoteAsync), $"Failed to process quote: {quote.ToJson()}", ex);
            }
        }

        private static IReadOnlyCollection<string> ValidateQuote(MtQuote quote)
        {
            var errors = new List<string>();

            if (quote == null)
            {
                errors.Add("Argument 'Order' is null.");
            }
            else
            {
                if (string.IsNullOrEmpty(quote.Instrument))
                {
                    errors.Add("Empty 'Instrument'");
                }
                if (quote.Date.Kind != DateTimeKind.Utc)
                {
                    errors.Add($"Invalid 'Date' Kind (UTC is required): '{quote.Date.Kind}'");
                }
            }

            return errors;
        }

        public void Dispose()
        {
            _subscriber.Dispose();
        }
    }
}