﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Walkabout.Data;

namespace Walkabout.StockQuotes
{

    public interface IStockQuoteService
    {
        /// <summary>
        /// Fetch updated security information for the given security.
        /// This can be called multiple times so the service needs to keep a queue of pending
        /// downloads.
        /// </summary>
        /// <param name="securities">List of securities to fetch </param>
        void BeginFetchQuote(string symbol);

        /// <summary>
        /// Return true if your service supports batch download of quotes, meaning one http
        /// request retrives multiple different quotes at once.  This is usually faster 
        /// than using BeginFetchQuote and is preferred, but if your service doesn't support
        /// this then BeginFetchQuotes will not be called.
        /// </summary>
        bool SupportsBatchQuotes { get; }

        /// <summary>
        /// Fetch updated security information for the given securities (most recent closing price).
        /// This can be called multiple times so the service needs to keep a queue of pending
        /// downloads.
        /// </summary>
        /// <param name="securities">List of securities to fetch </param>
        void BeginFetchQuotes(List<string> symbols);

        /// <summary>
        /// Return true if your service supports the UpdateHistory function.
        /// </summary>
        bool SupportsHistory { get; }

        /// <summary>
        /// If the stock quote service supports it, updates the given StockQuoteHistory
        /// with daily quotes back 20 years.
        /// </summary>
        /// <param name="symbol">The stock whose history is to be downloaded</param>
        /// <returns>The true if the history was updated or false if history is not found</returns>
        Task<bool> UpdateHistory(StockQuoteHistory history);

        /// <summary>
        /// Return a count of pending downloads.
        /// </summary>
        int PendingCount { get; }


        /// <summary>
        /// For the current session until all downloads are complete this returns the number of
        /// items completed from the batch provided in BeginFetchQuotes.  Once all downloads are
        /// complete this goes back to zero.
        /// </summary>
        int DownloadsCompleted { get; }

        /// <summary>
        /// Each downloaded quote is raised as an event on this interface.  Could be from any thread.
        /// </summary>
        event EventHandler<StockQuote> QuoteAvailable;

        /// <summary>
        /// Event means a given stock quote symbol was not found by the stock quote service.  
        /// </summary>
        event EventHandler<string> SymbolNotFound;

        /// <summary>
        /// If some error happens fetching a quote, this event is raised.
        /// </summary>
        event EventHandler<string> DownloadError;

        /// <summary>
        /// If the service is performing a whole batch at once, this event is raised after each batch is complete.
        /// If there are still more downloads pending the boolean value is raised with the value false.
        /// This is also raised when the entire pending list is completed with the boolean set to true.
        /// </summary>
        event EventHandler<bool> Complete;

        /// <summary>
        /// This event is raised if quota limits are stopping the service from responding right now.
        /// The booling is true when suspended, and false when resuming.
        /// </summary>
        event EventHandler<bool> Suspended;

        /// <summary>
        /// Stop all pending requests
        /// </summary>
        void Cancel();
    }

    /// <summary>
    /// This class encapsulates a new stock quote from IStockQuoteService, and is also
    /// designed for XML serialization
    /// </summary>
    public class StockQuote
    {
        public StockQuote() { }
        [XmlAttribute]
        public string Name { get; set; }
        [XmlAttribute]
        public string Symbol { get; set; }
        [XmlAttribute]
        public DateTime Date { get; set; }
        [XmlAttribute]
        public decimal Open { get; set; }
        [XmlAttribute]
        public decimal Close { get; set; }
        [XmlAttribute]
        public decimal High { get; set; }
        [XmlAttribute]
        public decimal Low { get; set; }
        [XmlAttribute]
        public decimal Volume { get; set; }
        [XmlAttribute]
        public DateTime Downloaded { get; set; }
    }

    /// <summary>
    /// A stock quote log designed for XML serialization
    /// </summary>
    public class StockQuoteHistory
    {
        public StockQuoteHistory() { History = new List<StockQuote>(); }

        public string Symbol { get; set; }

        /// <summary>
        /// Whether this is a partial or complete history.
        /// </summary>
        public bool Complete { get; set; }

        public List<StockQuote> History { get; set; }

        public DateTime MostRecentDownload
        {
            get
            {
                if (History != null && History.Count > 0)
                {
                    return History.Last().Downloaded;
                }
                return DateTime.MinValue;
            }
        }

        public List<StockQuote> GetSorted()
        {
            var result = new SortedDictionary<DateTime, StockQuote>();
            if (History != null)
            {
                foreach (var quote in History)
                {
                    result[quote.Date] = quote;
                }
            }
            return new List<StockQuote>(result.Values);
        }

        public bool AddQuote(StockQuote quote, bool replace=true)
        {
            if (History == null)
            {
                History = new List<StockQuote>();
            }
            int len = History.Count;
            for(int i = 0; i < len; i++)
            {
                var h = History[i];
                if (h.Date == quote.Date)
                {
                    // already have this one
                    if (replace)
                    {
                        h.Downloaded = quote.Downloaded;
                        h.Open = quote.Open;
                        h.Close = quote.Close;
                        h.High = quote.High;
                        h.Low = quote.Low;
                        h.Volume = quote.Volume;
                    }
                    return true;
                }
                if (h.Date > quote.Date)
                {
                    // keep it sorted by date
                    History.Insert(i, quote);
                    return true;
                }
            }
            History.Add(quote);
            return true;
        }

        public static StockQuoteHistory Load(string logFolder, string symbol)
        {
            var filename = System.IO.Path.Combine(logFolder, symbol + ".xml");
            if (System.IO.File.Exists(filename))
            {
                XmlSerializer s = new XmlSerializer(typeof(StockQuoteHistory));
                using (XmlReader r = XmlReader.Create(filename))
                {
                    return (StockQuoteHistory)s.Deserialize(r);
                }
            }
            return null;
        }

        public void Save(string logFolder)
        {
            var filename = System.IO.Path.Combine(logFolder, Symbol + ".xml");
            XmlSerializer s = new XmlSerializer(typeof(StockQuoteHistory));
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            using (XmlWriter w = XmlWriter.Create(filename, settings))
            {
                s.Serialize(w, this);
            }
        }

        internal void Merge(StockQuoteHistory newHistory)
        {
            foreach (var item in newHistory.History)
            {
                this.AddQuote(item);
            }
        }
    }

    /// <summary>
    /// </summary>
    public class StockServiceSettings : INotifyPropertyChanged
    {
        private string _name;
        private string _apiKey;
        private int _requestsPerMinute;
        private int _requestsPerDay;
        private int _requestsPerMonth;

        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged("Name");
                }
            }
        }

        public string ApiKey
        {
            get { return _apiKey; }
            set
            {
                if (_apiKey != value)
                {
                    _apiKey = value;
                    OnPropertyChanged("ApiKey");
                }
            }
        }

        public int ApiRequestsPerMinuteLimit
        {
            get { return _requestsPerMinute; }
            set
            {
                if (_requestsPerMinute != value)
                {
                    _requestsPerMinute = value;
                    OnPropertyChanged("ApiRequestsPerMinuteLimit");
                }
            }
        }

        public int ApiRequestsPerDayLimit
        {
            get { return _requestsPerDay; }
            set
            {
                if (_requestsPerDay != value)
                {
                    _requestsPerDay = value;
                    OnPropertyChanged("ApiRequestsPerDayLimit");
                }
            }
        }

        public int ApiRequestsPerMonthLimit
        {
            get { return _requestsPerMonth; }
            set
            {
                if (_requestsPerMonth != value)
                {
                    _requestsPerMonth = value;
                    OnPropertyChanged("ApiRequestsPerMonthLimit");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(name));
            }
        }

        public void Serialize(XmlWriter w)
        {
            w.WriteElementString("Name", this.Name == null ? "" : this.Name);
            w.WriteElementString("ApiKey", this.ApiKey == null ? "" : this.ApiKey);
            w.WriteElementString("ApiRequestsPerMinuteLimit", this.ApiRequestsPerMinuteLimit.ToString());
            w.WriteElementString("ApiRequestsPerDayLimit", this.ApiRequestsPerDayLimit.ToString());
            w.WriteElementString("ApiRequestsPerMonthLimit", this.ApiRequestsPerMonthLimit.ToString());
        }

        public void Deserialize(XmlReader r)
        {
            if (r.IsEmptyElement) return;
            while (r.Read() && !r.EOF && r.NodeType != XmlNodeType.EndElement)
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (r.Name == "Name")
                    {
                        this.Name = r.ReadElementContentAsString();
                    }
                    else if (r.Name == "ApiKey")
                    {
                        this.ApiKey = r.ReadElementContentAsString();
                    }
                    else if (r.Name == "ApiRequestsPerMinuteLimit")
                    {
                        this.ApiRequestsPerMinuteLimit = r.ReadElementContentAsInt();
                    }
                    else if (r.Name == "ApiRequestsPerDayLimit")
                    {
                        this.ApiRequestsPerDayLimit = r.ReadElementContentAsInt();
                    }
                    else if (r.Name == "ApiRequestsPerMonthLimit")
                    {
                        this.ApiRequestsPerMonthLimit = r.ReadElementContentAsInt();
                    }
                }
            }
        }

    }


}
