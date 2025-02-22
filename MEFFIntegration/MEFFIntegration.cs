﻿/* Copyright (C) 2013 Fairmat SRL (info@fairmat.com, http://www.fairmat.com/)
 * Author(s): Stefano Angeleri (stefano.angeleri@fairmat.com)
 *            Matteo Tesser (matteo.tesser@fairmat.com)
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using DVPLI;
using DVPLI.Interfaces;
using DVPLI.MarketDataTypes;
using OptionQuotes;
using TickersUtils;

namespace MEFFIntegration
{
    /// <summary>
    /// Implements the interface to provide Fairmat access to the
    /// MEFF Market Data Provider.
    /// </summary>
    /// <remarks>
    /// This Data Market Provider supports only Scalar requests.
    /// </remarks>
    [Mono.Addins.Extension("/Fairmat/MarketDataProvider")]
    public class MEFFIntegration : IMarketDataProvider, IDescription, ITickersInfo, IMarketDataProviderInfo
    {
        #region IDescription Implementation

        /// <summary>
        /// Gets an user friendly description of the service provided by this class.
        /// In this case "MEFF".
        /// </summary>
        public string Description
        {
            get
            {
                return "MEFF";
            }
        }

        #endregion IDescription Implementation

        #region ITickersInfo Implementation

        /// <summary>
        /// Returns the list of the tickers currently supported by this market data provider.
        /// </summary>
        /// <param name="filter">The filter to use to choose which symbols to return.</param>
        /// <returns>The supported ticker array.</returns>
        public ISymbolDefinition[] SupportedTickers(string filter = null)
        {
            List<string> tickers = new List<string>(MEFFAPI.GetTickerList());
            List<ISymbolDefinition> symbols = new List<ISymbolDefinition>();

            // Apply a filter if requested.
            if (filter != null)
            {
                tickers = tickers.FindAll(x => x.StartsWith(filter));
            }

            // Put all in Symbol Definition Entries.
            tickers.ForEach(x => symbols.Add(new SymbolDefinition(x, "MEFF Market Equity")));

            return symbols.ToArray();
        }

        #endregion ITickersInfo Implementation

        #region IMarketDataProviderInfo Implementation

        /// <summary>
        /// Returns information about the data providers capabilities.
        /// </summary>
        /// <param name="category">The required data category.</param>
        /// <returns>The data category access type.</returns>
        public MarketDataAccessType GetDataAvailabilityInfo(MarketDataCategory category)
        {
            switch (category)
            {
                case MarketDataCategory.EquityPrice:
                    {
                        return MarketDataAccessType.Local;
                    }

                case MarketDataCategory.EquityVolatilitySurface:
                    {
                        return MarketDataAccessType.Local;
                    }

                default:
                    {
                        return MarketDataAccessType.NotAvailable;
                    }
            }
        }

        #endregion IMarketDataProviderInfo Implementation

        #region IMarketDataProvider Implementation

        /// <summary>
        /// Sets the credentials to use to access this Market Data Provider.
        /// </summary>
        /// <remarks>
        /// This is part of the interface <see cref="IMarketDataProvider"/>
        /// but it's currently unused.
        /// </remarks>
        public string Credentials
        {
            set
            {
                // Ignore any set as the data is not needed.
            }
        }

        /// <summary>
        /// Gets the market data from a single day.
        /// </summary>
        /// <param name="mdq">
        /// A <see cref="MarketDataQuery"/> with the data request.
        /// </param>
        /// <param name="marketData">
        /// In case of success, the requested market data as <see cref="IMarketData"/>.
        /// </param>
        /// <returns>
        /// A <see cref="RefreshStatus"/> indicating if the query was successful.
        /// </returns>
        public RefreshStatus GetMarketData(MarketDataQuery mdq, out IMarketData marketData)
        {
            if (mdq.MarketDataType == typeof(Fairmat.MarketData.CallPriceMarketData).ToString())
            {
                return GetCallPriceMarketData(mdq, out marketData);
            }

            // Reuse the Historical time series to get the single quote
            // (Equal start/end date = get the quote of the day).
            DateTime[] dates;
            IMarketData[] marketDataArray;
            RefreshStatus status = GetTimeSeries(mdq, mdq.Date, out dates, out marketDataArray);

            // If there were errors already just report them back.
            if (status.HasErrors)
            {
                marketData = null;
            }
            else
            {
                // If all was succesful try to prepare data for this type of query.
                status = new RefreshStatus();

                // Do some sanity check on the gathered data.
                if (marketDataArray.Length != 1 && dates.Length != 1 && dates[0] != mdq.Date)
                {
                    status.HasErrors = true;
                    status.ErrorMessage += "GetMarketData: Requested date " +
                                           "or Market Data not available.";
                    marketData = null;
                }
                else
                {
                    // If they pass just take the first element as result
                    // (which must be also the only one).
                    marketData = marketDataArray[0];
                }
            }

            return status;
        }

        /// <summary>
        /// Gets a series of Historical Market Data from the starting date
        /// to the end date.
        /// </summary>
        /// <param name="mdq">
        /// A <see cref="MarketDataQuery"/> with the data request.
        /// </param>
        /// <param name="end">
        /// A <see cref="DateTime"/> with the ending date of the period to fetch data from.
        /// </param>
        /// <param name="dates">
        /// In case of success, a list of the dates data was fetched from in the requested period.
        /// </param>
        /// <param name="marketData">
        /// In case of success, a list of the fetched market data day
        /// by day corresponding to <see cref="dates"/>.
        /// </param>
        /// <returns>
        /// A <see cref="RefreshStatus"/> indicating if the query was successful.
        /// </returns>
        public RefreshStatus GetTimeSeries(MarketDataQuery mdq, DateTime end, out DateTime[] dates, out IMarketData[] marketData)
        {
            RefreshStatus status = new RefreshStatus();

            // Check if close value was requested.
            switch (mdq.Field)
            {
                case "close":
                    {
                        break;
                    }

                default:
                    {
                        // In case the request is not close return an error.
                        marketData = null;
                        dates = null;
                        status.HasErrors = true;
                        status.ErrorMessage += "GetTimeSeries: Market data not available (only " +
                                               "close values are available, " +
                                               mdq.Field + " was requested).";
                        return status;
                    }
            }

            // For now only Scalar requests are handled.
            if (mdq.MarketDataType == typeof(Scalar).ToString())
            {
                List<MEFFHistoricalQuote> quotes = null;

                try
                {
                    // Request the data to the Market Data Provider.
                    quotes = MEFFAPI.GetHistoricalQuotes(TickerUtility.PreparseSymbol(mdq.Ticker), mdq.Date, end);
                }
                catch (Exception e)
                {
                    // There can be conversion, server availability
                    // and other exceptions during this request.
                    marketData = null;
                    dates = null;
                    status.HasErrors = true;
                    status.ErrorMessage += "GetTimeSeries: Market data not available due " +
                                           "to problems with MEFF service: " + e.Message;
                    return status;
                }

                // Check if there is at least one result.
                if (quotes.Count >= 1)
                {
                    // Allocate the structures for the output.
                    marketData = new Scalar[quotes.Count];
                    dates = new DateTime[quotes.Count];

                    // Scan the list of quotes to prepare the data for Fairmat.
                    for (int i = 0; i < quotes.Count; i++)
                    {
                        // Fill the dates array from the date field of each quote.
                        dates[i] = quotes[i].SessionDate;

                        // Prepare the single scalar data.
                        Scalar val = new Scalar();
                        val.TimeStamp = quotes[i].SessionDate;
                        val.Value = quotes[i].SettlPrice;

                        // Put it in the output structure.
                        marketData[i] = val;
                    }

                    return status;
                }
                else
                {
                    // If there isn't at least one result return an error.
                    marketData = null;
                    dates = null;
                    status.HasErrors = true;
                    status.ErrorMessage += "GetTimeSeries: Market data not available: " +
                                           "empty data set for the request.";
                    return status;
                }
            }
            else
            {
                // If control falls through here it means the request type was not supported.
                marketData = null;
                dates = null;
                status.HasErrors = true;
                status.ErrorMessage += "GetTimeSeries: Market data request type (" +
                                       mdq.MarketDataType +
                                       ") not supported by the Market Data Provider.";
                return status;
            }
        }

        /// <summary>
        /// Checks if the MEFF service is reachable
        /// and answers to requests correctly.
        /// </summary>
        /// <remarks>
        /// This is done by requesting a well known quote and checking if data is returned,
        /// the sanity of the data is not checked.
        /// </remarks>
        /// <returns>
        /// A <see cref="Status"/> indicating if the
        /// MEFF service is working.
        /// </returns>
        public Status TestConnectivity()
        {
            // Prepare the default result, in case everything will go well.
            Status state = new Status();
            state.HasErrors = false;
            state.ErrorMessage = string.Empty;

            try
            {
                // Try simply requesting a single data series known to exist
                // and to produce 1 result (we use GRF at 31 jan 2011).
                List<MEFFHistoricalQuote> quotes = MEFFAPI.GetHistoricalQuotes("GRF",
                                                                               new DateTime(2011, 1, 31),
                                                                               new DateTime(2011, 1, 31));

                if (quotes.Count != 1)
                {
                    // If there is a number of results different than 1,
                    // it means the service is not answering as expected,
                    // so fail the test.
                    state.HasErrors = true;
                    state.ErrorMessage = "Data from MEFF not available or unreliable.";
                }
            }
            catch (Exception e)
            {
                // If an exception was thrown during data fetching it means
                // there is a problem with the service (either timeout,
                // connection failure, or MEFF changed data format).
                state.HasErrors = true;
                state.ErrorMessage = "Unable to connect to MEFF service: " + e.Message;
            }

            return state;
        }

        /// <summary>
        /// Retrieves available call and put options for a given ticker.
        /// </summary>
        /// <param name="mdq">The market data query.</param>
        /// <param name="marketData">The requested market data.</param>
        /// <returns>The result of the query.</returns>
        private RefreshStatus GetCallPriceMarketData(MarketDataQuery mdq, out IMarketData marketData)
        {
            marketData = null;
            Fairmat.MarketData.CallPriceMarketData data = new Fairmat.MarketData.CallPriceMarketData();

            List<MEFFHistoricalQuote> options = MEFFAPI.GetOptions(mdq.Ticker, mdq.Date);

            foreach (MEFFHistoricalQuote q in options)
            {
                Console.WriteLine(q.ContractCode + "\t" + q.StrikePrice + "\t" + q.MaturityDate.ToShortDateString() + "\t" + q.SettlPrice);
            }

            var status = OptionQuotesUtility.GetCallPriceMarketData(this, mdq, options.ConvertAll(x => (OptionQuotes.IOptionQuote)x), data);
            if (status.HasErrors)
            {
                return status;
            }

            marketData = data;
            Console.WriteLine(data);
            return status;
        }

        #endregion IMarketDataProvider Implementation
    }
}
