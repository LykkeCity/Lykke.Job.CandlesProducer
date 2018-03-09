﻿using System.Linq;
using System.Threading.Tasks;
using Common;
using JetBrains.Annotations;
using Lykke.Job.CandlesProducer.Core.Domain.Trades;
using Lykke.Job.CandlesProducer.Core.Services;
using Lykke.Job.CandlesProducer.Core.Services.Assets;
using Lykke.Job.CandlesProducer.Core.Services.Candles;
using Lykke.Job.CandlesProducer.Core.Services.Trades;
using Lykke.Job.CandlesProducer.Services.Trades.Spot.Messages;

namespace Lykke.Job.CandlesProducer.Services.Trades.Spot
{
    [UsedImplicitly]
    public class SpotTradesSubscriber : ITradesSubscriber
    {
        private readonly ICandlesManager _candlesManager;
        private readonly IRabbitMqSubscribersFactory _subscribersFactory;
        private readonly IRabbitSubscriptionSettings _tradesSubscriptionSettings;
        private readonly IAssetPairsManager _assetPairsManager;
        private IStopable _limitTradesSubscriber;

        public SpotTradesSubscriber(
            ICandlesManager candlesManager, 
            IRabbitMqSubscribersFactory subscribersFactory, 
            IRabbitSubscriptionSettings tradesSubscriptionSettings,
            IAssetPairsManager assetPairsManager)
        {
            _candlesManager = candlesManager;
            _subscribersFactory = subscribersFactory;
            _tradesSubscriptionSettings = tradesSubscriptionSettings;
            _assetPairsManager = assetPairsManager;
        }

        public void Start()
        {
            _limitTradesSubscriber = _subscribersFactory.Create<LimitOrdersMessage>(_tradesSubscriptionSettings.ConnectionString, "lykke", "limitorders.clients", ProcessLimitTradesAsync);
        }

        private async Task ProcessLimitTradesAsync(LimitOrdersMessage message)
        {
            if (message.Orders == null || !message.Orders.Any())
            {
                return;
            }

            var limitOrderIds = message.Orders
                .Select(o => o.Order.Id)
                .ToHashSet();

            foreach (var orderMessage in message.Orders)
            {
                if (orderMessage.Trades == null)
                {
                    continue;
                }

                foreach (var tradeMessage in orderMessage.Trades)
                {
                    var assetPair = _assetPairsManager.TryGetEnabledPairAsync(orderMessage.Order.AssetPairId)
                        .GetAwaiter()
                        .GetResult();

                    // If both orders of the trade are limit, then both of them should be contained in the single message,
                    // this is by design.

                    var isOppositeOrderIsLimit = limitOrderIds.Contains(tradeMessage.OppositeOrderId);

                    // If opposite order is market order, then unconditionally takes the given limit order.
                    // But if both of orders are limit orders, we should take only one of them.

                    if (isOppositeOrderIsLimit)
                    {
                        var isBuyOrder = orderMessage.Order.Volume > 0;

                        // Takes trade only for the sell limit orders

                        if (isBuyOrder)
                        {
                            continue;
                        }
                    }

                    // Volumes in the asset pair base and quoting assets
                    double baseVolume;
                    double quotingVolume;

                    if (tradeMessage.Asset == assetPair.BaseAssetId)
                    {
                        baseVolume = tradeMessage.Volume;
                        quotingVolume = tradeMessage.OppositeVolume;
                    }
                    else
                    {
                        baseVolume = tradeMessage.OppositeVolume;
                        quotingVolume = tradeMessage.Volume;
                    }

                    var trade = new Trade(
                        orderMessage.Order.AssetPairId,
                        tradeMessage.Timestamp,
                        baseVolume,
                        quotingVolume,
                        tradeMessage.Price
                    );

                    await _candlesManager.ProcessTradeAsync(trade);
                }
            }
        }
        
        public void Stop()
        {
            _limitTradesSubscriber?.Stop();
        }

        public void Dispose()
        {
            _limitTradesSubscriber?.Dispose();
        }
    }
}
