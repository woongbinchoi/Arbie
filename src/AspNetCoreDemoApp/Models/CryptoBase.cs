using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace AspNetCoreDemoApp.Models
{

	public struct CCResult
	{
		public decimal price;
		public string status;
	}

	public class CCResultMulti
	{
		public Dictionary<string, dynamic> PriceDict;

		public CCResultMulti()
		{
			PriceDict = new Dictionary<string, dynamic>();
		}
	}

	public class CryptoBase
	{
		private Dictionary<string, decimal> ExchangeList;
		private List<string> TopMarketList;
		private List<string> TopCoinList;

		private HttpClient CoinAPIClient; // provide List of Top Markets
		private HttpClient CoinMarketCapClient; // provide List of Top Coins
		private HttpClient ExchangeRateClient; // provide Exchange List
		private HttpClient CoinHillsClient; // provide cspa: Average Coin Price per country
		private HttpClient CryptoCompareClient; // provide Coin Price per market


		public CryptoBase()
		{
			TopMarketList = new List<string>();
			TopCoinList = new List<string>();
			ExchangeList = new Dictionary<string, decimal>();

			ServerInit();

			UpdateMarketList();
			UpdateCoinList();
			UpdateExchangeList();
		}

		private HttpClient SetClient(string uri, string publickey = "")
		{
			HttpClient client = new HttpClient();
			client.BaseAddress = new Uri(uri);
			// Add an Accept header for JSON format.  
			client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (publickey != "")
			{
				client.DefaultRequestHeaders.Add("X-CoinAPI-Key", publickey);
			}
			return client;
		}

		private void ServerInit()
		{
			CoinAPIClient = SetClient("https://rest.coinapi.io/", "DCD1B5F3-520F-4FC6-8713-EDF5DBAF9AE7");
			CoinMarketCapClient = SetClient("https://api.coinmarketcap.com/");
			ExchangeRateClient = SetClient("https://exchangeratesapi.io/api/");
			CoinHillsClient = SetClient("https://api.coinhills.com/");
			CryptoCompareClient = SetClient("https://min-api.cryptocompare.com/");
		}

		public void UpdateMarketList()
		{
			HttpResponseMessage response = CoinAPIClient.GetAsync("v1/exchanges").Result;
			if (response.IsSuccessStatusCode)
			{
				string products = response.Content.ReadAsStringAsync().Result;
				var values = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(products);
				foreach (Dictionary<string, string> dict in values)
				{
					string MarketName = dict["name"].Split(' ')[0];
					if (!TopMarketList.Contains(MarketName)) TopMarketList.Add(MarketName);
				}
			}
			else
			{
				Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
			}
		}

		public void UpdateCoinList()
		{
			HttpResponseMessage response = CoinMarketCapClient.GetAsync("v1/ticker/?limit=20").Result;
			if (response.IsSuccessStatusCode)
			{
				string products = response.Content.ReadAsStringAsync().Result;
				var values = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(products);
				foreach (Dictionary<string, string> dict in values)
				{
					string CoinSymbol = dict["symbol"];
					CoinSymbol = CoinSymbol == "MIOTA" ? "IOT" : CoinSymbol;
					TopCoinList.Add(CoinSymbol);
				}
			}
			else
			{
				Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
			}
		}

		public void UpdateExchangeList()
		{
			HttpResponseMessage response = ExchangeRateClient.GetAsync("latest?base=USD").Result;
			if (response.IsSuccessStatusCode)
			{
				string products = response.Content.ReadAsStringAsync().Result;
				Dictionary<string, dynamic> values = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(products);
				Newtonsoft.Json.Linq.JObject rateslist = values["rates"];
				ExchangeList = rateslist.ToObject<Dictionary<string, decimal>>();
			}
			else
			{
				Console.WriteLine("{0} ({1})", (int)response.StatusCode, response.ReasonPhrase);
			}
		}

		public bool CoinHillsQuery(string CryptoCurrSym, string FiatCurrSym, ref CCResult result)
		{
			string CryptoCoin = CryptoCurrSym.ToLower();
			string FiatCoin = FiatCurrSym.ToLower();
			HttpResponseMessage Response = CoinHillsClient.GetAsync("v1/cspa/" + CryptoCoin + "/" + FiatCoin).Result;
			if (Response.IsSuccessStatusCode)
			{
				string Product = Response.Content.ReadAsStringAsync().Result;
				dynamic ParsedProduct = Newtonsoft.Json.Linq.JObject.Parse(Product);
				if ((bool)ParsedProduct["success"])
				{
					string CSPAString = "CSPA:" + CryptoCoin.ToUpper() + "/" + FiatCoin.ToUpper();
					result.price = (decimal)ParsedProduct["data"][CSPAString]["cspa"];
					result.status = "OK.";
					return true;
				}
				else
				{
					result.price = 0;
					result.status = ParsedProduct["message"];
					return false;
				}
			}
			else
			{
				result.price = 0;
				result.status = Response.ReasonPhrase;
				return false;
			}
		}

		public bool CryptoCompareMultiGlobalQuery(string FromCurrSyms, string ToCurrSyms, ref CCResultMulti result)
		{
			string FromUpper = FromCurrSyms.ToUpper();
			string ToUpper = ToCurrSyms.ToUpper();

			HttpResponseMessage Response = CryptoCompareClient.GetAsync("data/pricemulti?fsyms=" + FromUpper + "&tsyms=" + ToUpper).Result;
			if (Response.IsSuccessStatusCode)
			{
				string Product = Response.Content.ReadAsStringAsync().Result;
				var Values = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(Product);
				if (Values.ContainsKey("Response") && Values["Response"] == "Error")
				{
					result.PriceDict.Add("Status", Values["Message"]);
					return false;
				}
				else
				{
					var NewValue = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, decimal>>>(Product);
					foreach (KeyValuePair<string, Dictionary<string, decimal>> kvp in NewValue)
					{
						result.PriceDict.Add(kvp.Key, kvp.Value);
					}
					return true;
				}
			}
			else
			{
				result.PriceDict.Add("Status", Response.ReasonPhrase);
				return false;
			}
		}

		public bool CryptoCompareQuery(string FromCurrSym, string ToCurrSym, string Market, ref CCResult result)
		{
			string FromUpper = FromCurrSym.ToUpper();
			string ToUpper = ToCurrSym.ToUpper();
			HttpResponseMessage Response = CryptoCompareClient.GetAsync("data/price?fsym=" + FromUpper + "&tsyms=" + ToUpper + "&e=" + Market).Result;
			if (Response.IsSuccessStatusCode)
			{
				string Product = Response.Content.ReadAsStringAsync().Result;
				var Values = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(Product);
				if (Values.ContainsKey(ToUpper))
				{
					result.price = (decimal)Values[ToUpper];
					result.status = "OK.";
					return true;
				}
				else
				{
					result.price = 0;
					result.status = Values["Message"];
					return false;
				}
			}
			else
			{
				result.price = 0;
				result.status = Response.ReasonPhrase;
				return false;
			}
		}

		private decimal doCompareCurrTwoMarket(string FromCurrSym, string ToCurrSym, string FromMarket, string ToMarket)
		{
			CCResult FromData = new CCResult();
			CCResult ToData = new CCResult();
			decimal Arbitrage = -420;
			if (CryptoCompareQuery(FromCurrSym, ToCurrSym, FromMarket, ref FromData) &&
				CryptoCompareQuery(FromCurrSym, ToCurrSym, ToMarket, ref ToData))
			{
				Arbitrage = Math.Round((FromData.price / ToData.price - 1) * 100, 4);
			}
			return Arbitrage;
		}

		public string ListCurrTwoMarket(string FromMarket, string ToMarket) //Make arbitrage list between two markets: From Top 10 CCs to BTC,USD
		{
			List<KeyValuePair<string, decimal>> ArbList = new List<KeyValuePair<string, decimal>>();
			foreach (string CryptoCoin in TopCoinList.Take(10))
			{
				decimal BTCArb = doCompareCurrTwoMarket(CryptoCoin, "BTC", FromMarket, ToMarket);
				decimal USDArb = doCompareCurrTwoMarket(CryptoCoin, "USD", FromMarket, ToMarket);
				if (CryptoCoin != "BTC" && BTCArb != -420)
				{
					ArbList.Add(new KeyValuePair<string, decimal>(CryptoCoin + "->BTC", BTCArb));
				}
				if (USDArb != -420)
				{
					ArbList.Add(new KeyValuePair<string, decimal>(CryptoCoin + "->USD", USDArb));
				}
			}

			if (ArbList.Count > 0)
			{
				string result = "Results found." + Environment.NewLine + "Buying from : " + FromMarket.ToUpper() +
					Environment.NewLine + "Selling to : " + ToMarket.ToUpper() + Environment.NewLine + Environment.NewLine;
				var ordered = ArbList.OrderByDescending(pair => pair.Value);
				foreach (KeyValuePair<string, decimal> kvp in ordered)
				{
					result += kvp.Key + " : " + kvp.Value + "%" + Environment.NewLine;
				}
				decimal firstval = ordered.First().Value;
				result += Environment.NewLine + "Best Arbitrage:" + Environment.NewLine;
				result += firstval >= 0 ? ordered.First().Key + " with gain of " + firstval + "%."
					: ordered.First().Key + " with lose of " + firstval + "%.";
				return result;
			}
			else
			{
				return "No arbitrage results found between two markets requested: " + FromMarket + ", " + ToMarket;
			}
		}

		public string ListMarketTwoCoin(string FromCurr, string ToCurr)
		{
			List<KeyValuePair<string, decimal>> ArbList = new List<KeyValuePair<string, decimal>>(); // Map from Market to Arbitrage
			foreach (string Market in TopMarketList.Take(40)) // Only querying 40 
			{
				CCResult PriceData = new CCResult();
				if (CryptoCompareQuery(FromCurr, ToCurr, Market, ref PriceData))
				{
					ArbList.Add(new KeyValuePair<string, decimal>(Market, PriceData.price));
				}
			}

			string FC = FromCurr.ToUpper();
			string TC = ToCurr.ToUpper();

			if (ArbList.Count > 0)
			{

				string result = "Results found." + Environment.NewLine +
					"Buying " + FC + " with " + TC + Environment.NewLine + Environment.NewLine;
				var ordered = ArbList.OrderBy(pair => pair.Value);
				foreach (KeyValuePair<string, decimal> kvp in ordered)
				{
					result += kvp.Key + " : " + kvp.Value + " " + TC + Environment.NewLine;
				}
				result += Environment.NewLine + "Best Deal is buying from " + ordered.First().Key +
					Environment.NewLine + "Price: " + ordered.First().Value + " " + TC + ".";
				return result;
			}
			else
			{
				return "No arbitrage results found between two coins requested: " + FC + ", " + TC;
			}
		}

		public string ListCountryCointoUSD(string CryptoCurr)
		{
			CCResultMulti Result = new CCResultMulti();
			string CurrencyList = "";
			foreach (KeyValuePair<string, decimal> kvp in ExchangeList)
			{
				if (kvp.Key != "MYR" && kvp.Key != "ZAR" && kvp.Key != "THB" && kvp.Key != "TRY" && kvp.Key != "DKK" && kvp.Key != "ISK" && kvp.Key != "HRK" && kvp.Key != "RON")
				{
					CurrencyList += kvp.Key + ",";
				}
			}

			string result = "";
			if (CryptoCompareMultiGlobalQuery(CryptoCurr, CurrencyList, ref Result))
			{
				List<KeyValuePair<string, decimal>> ArbList = new List<KeyValuePair<string, decimal>>();
				foreach (KeyValuePair<string, decimal> kvp in Result.PriceDict[CryptoCurr.ToUpper()])
				{
					decimal newval = kvp.Key == "USD" ? 1 : kvp.Value / ExchangeList[kvp.Key] / Result.PriceDict[CryptoCurr.ToUpper()]["USD"];
					decimal Arbitrage = Math.Round((newval - 1) * 100, 4);
					ArbList.Add(new KeyValuePair<string, decimal>(CryptoCurr.ToUpper() + "->" + kvp.Key, Arbitrage));
				}
				var ordered = ArbList.OrderByDescending(pair => pair.Value);

				result += "Results found. " + Environment.NewLine +
					"Each calculated rate is a price relative to average market price in US." + Environment.NewLine;
				foreach (KeyValuePair<string, decimal> kvp in ordered)
				{
					result += kvp.Key + " : " + kvp.Value + "%" + Environment.NewLine;
				}
				string firstkey = ordered.First().Key;
				decimal firstval = ordered.First().Value;
				result += Environment.NewLine + "Best Arbitrage Opportunity:" + Environment.NewLine;
				result += firstval >= 0 ? firstkey + " with gain of " + firstval + "%."
					: firstkey + " with lose of " + firstval + "%.";
			}
			else
			{
				result = "Error occured during the search." + Environment.NewLine + Result.PriceDict["Status"];
			}
			return result;
		} // DONE

		public string ListCoinTwoCountry(string FromCountry, string ToCountry)
		{
			var FromUpper = FromCountry.ToUpper();
			var ToUpper = ToCountry.ToUpper();
			CCResultMulti Result = new CCResultMulti();
			string CoinList = "";
			foreach (string CryptoCoin in TopCoinList)
			{
				CoinList += CryptoCoin + ",";
			}
			CoinList = CoinList.Substring(0, CoinList.Length - 1);

			string result = "";
			if (CryptoCompareMultiGlobalQuery(CoinList, FromCountry + "," + ToCountry, ref Result))
			{
				List<KeyValuePair<string, decimal>> ArbList = new List<KeyValuePair<string, decimal>>();
				var PriceDict = Result.PriceDict;
				foreach (string CryptoCoin in TopCoinList)
				{
					if (PriceDict.ContainsKey(CryptoCoin) && PriceDict[CryptoCoin].Count == 2)
					{
						decimal FromPrice = PriceDict[CryptoCoin][FromUpper];
						decimal ToPrice = PriceDict[CryptoCoin][ToUpper];
						decimal FromPriceConverted = FromUpper == "USD" ? FromPrice : FromPrice / ExchangeList[FromUpper];
						decimal ToPriceConverted = ToUpper == "USD" ? ToPrice : ToPrice / ExchangeList[ToUpper];
						decimal Arbitrage = Math.Round((FromPriceConverted / ToPriceConverted - 1) * 100, 4);
						ArbList.Add(new KeyValuePair<string, decimal>(CryptoCoin, Arbitrage));
					}
				}
				var ordered = ArbList.OrderByDescending(pair => pair.Value);

				result += "Results found. " + Environment.NewLine +
					"Below are arbitrage rates for the 20 most popular crypto-currencies." + Environment.NewLine +
					"Buying from : " + FromUpper + Environment.NewLine + "Selling to : " + ToUpper +
					Environment.NewLine + Environment.NewLine;
				foreach (KeyValuePair<string, decimal> kvp in ordered)
				{
					result += kvp.Key + " : " + kvp.Value + "%" + Environment.NewLine;
				}
				string firstkey = ordered.First().Key;
				decimal firstval = ordered.First().Value;
				result += Environment.NewLine + "Best Arbitrage Opportunity:" + Environment.NewLine;
				result += firstval >= 0 ? firstkey + " with gain of " + firstval + "%."
					: firstkey + " with lose of " + firstval + "%.";
			}
			else
			{
				result = "Error occured during the search." + Environment.NewLine + Result.PriceDict["Status"];
			}
			return result;
		}

		public string[] ListCoinListCountry()
		{
			CCResultMulti Result = new CCResultMulti();
			string CurrencyList = "";
			foreach (KeyValuePair<string, decimal> kvp in ExchangeList)
			{
				if (kvp.Key != "MYR" && kvp.Key != "ZAR" && kvp.Key != "THB" && kvp.Key != "TRY" && kvp.Key != "DKK" && kvp.Key != "ISK" && kvp.Key != "HRK" && kvp.Key != "RON")
				{
					CurrencyList += kvp.Key + ",";
				}
			}

			string CoinList = "";
			foreach (string CryptoCoin in TopCoinList)
			{
				CoinList += CryptoCoin + ",";
			}
			CoinList = CoinList.Substring(0, CoinList.Length - 1);

			List<string> responses = new List<string>();

			string result = "";
			if (CryptoCompareMultiGlobalQuery(CoinList, CurrencyList, ref Result))
			{
				List<KeyValuePair<string, decimal>> ArbList = new List<KeyValuePair<string, decimal>>();
				var PriceDict = Result.PriceDict;

				foreach (KeyValuePair<string, dynamic> kvp in PriceDict)
				{
					string CryptoCurrKey = kvp.Key;
					foreach (KeyValuePair<string, decimal> kvpp in kvp.Value)
					{
						decimal newval = kvpp.Key == "USD" ? 1 : kvpp.Value / ExchangeList[kvpp.Key] / Result.PriceDict[CryptoCurrKey]["USD"];
						decimal Arbitrage = Math.Round((newval - 1) * 100, 4);
						ArbList.Add(new KeyValuePair<string, decimal>(CryptoCurrKey + "->" + kvpp.Key, Arbitrage));
					}
				}
				var ordered = ArbList.OrderByDescending(pair => pair.Value);

				result += "Results found. " + Environment.NewLine +
					"Below are top 100 results of arbitrage rates for the 20 most popular crypto-currencies across the world."
					+ Environment.NewLine + "Each calculated rate is a price relative to average market price in US."
					+ Environment.NewLine + "{XXX}->{YYY} means buying XXX coin in YYY country.";

				responses.Add(result);
				result = "";

				int index = 1;
				foreach (KeyValuePair<string, decimal> kvp in ordered)
				{
					result += index + ". " +kvp.Key + " : " + kvp.Value + "%" + Environment.NewLine;
					if (index % 20 == 0)
					{
						responses.Add(result);
						result = "";
					}
					++index;

					if (index > 100)
					{
						break;
					}
				}

				responses.Add(result);
				result = "";

				string firstkey = ordered.First().Key;
				decimal firstval = ordered.First().Value;
				result += Environment.NewLine + "Best Arbitrage Opportunity is:" + Environment.NewLine;
				result += firstval >= 0 ? firstkey + " with gain of " + firstval + "%."
					: firstkey + " with lose of " + firstval + "%.";

				responses.Add(result);
			}
			else
			{
				responses.Add("Error occured during the search." + Environment.NewLine + Result.PriceDict["Status"]);
			}
			return responses.ToArray();
		}

		public string CompareCurrTwoCountry(string CurrSym, string FromCountry, string ToCountry)
		{
			string response;
			CCResult FromData = new CCResult();
			CCResult ToData = new CCResult();
			decimal Arbitrage = -1;
			if (!CoinHillsQuery(CurrSym, FromCountry, ref FromData))
			{
				response = "Error occured during the search." + Environment.NewLine + FromData.status;
			}
			else if (!CoinHillsQuery(CurrSym, ToCountry, ref ToData))
			{
				response = "Error occured during the search." + Environment.NewLine + ToData.status;
			}
			else
			{
				string FC = FromCountry.ToUpper();
				string TC = ToCountry.ToUpper();
				string CS = CurrSym.ToUpper();
				decimal FromPriceAdjusted = Math.Round(FromData.price, 4);
				decimal ToPriceAdjusted = Math.Round(ToData.price, 4);
				decimal FromPriceConverted = FC == "USD" ? FromData.price : FromData.price / ExchangeList[FC];
				decimal ToPriceConverted = TC == "USD" ? ToData.price : ToData.price / ExchangeList[TC];
				decimal FromPriceConvertedAdjusted = Math.Round(FromPriceConverted, 4);
				decimal ToPriceConvertedAdjusted = Math.Round(ToPriceConverted, 4);


				Arbitrage = Math.Round((FromPriceConverted / ToPriceConverted - 1) * 100, 4);
				response = "Results found." + Environment.NewLine + "The converted price of " + CS + " in " +
					FC + " market is " + FromPriceConvertedAdjusted + " USD. (" + FromPriceAdjusted + " " + FC + ")" + Environment.NewLine +
					"The converted price of " + CS + " in " + TC + " market is " + ToPriceConvertedAdjusted +
					" USD. (" + ToPriceAdjusted + " " + TC + ")" + Environment.NewLine +
					"Arbitrage from " + FC + " to " + TC + " would be ";
				response = Arbitrage >= 0 ? response + "a gain of " + Arbitrage + "%."
					: response + "a lose of " + Arbitrage + "%.";
			}
			return response;
		}//DONE

		public string CompareCurrTwoMarket(string FromCurrSym, string ToCurrSym, string FromMarket, string ToMarket)
		{
			CCResult FromData = new CCResult();
			CCResult ToData = new CCResult();
			decimal Arbitrage = -1;
			string response;
			if (!CryptoCompareQuery(FromCurrSym, ToCurrSym, FromMarket, ref FromData))
			{
				//Console.WriteLine(FromData.status);
				response = "Error occured during the search." + Environment.NewLine + FromData.status;
			}
			else if (!CryptoCompareQuery(FromCurrSym, ToCurrSym, ToMarket, ref ToData))
			{
				//Console.WriteLine(ToData.status);
				response = "Error occured during the search." + Environment.NewLine + ToData.status;
			}
			else
			{
				Arbitrage = Math.Round((FromData.price / ToData.price - 1) * 100, 4);
				string FCS = FromCurrSym.ToUpper();
				string TCS = ToCurrSym.ToUpper();
				string FM = FromMarket.ToUpper();
				string TM = ToMarket.ToUpper();

				response = "Results found." + Environment.NewLine + "The price of (" +
					FCS + "-" + TCS + ") pair in " + FM + " is " + FromData.price + " " + TCS + "." +
					Environment.NewLine + "The price of (" +
					FCS + "-" + TCS + ") pair in " + TM + " is " + ToData.price + " " + TCS + "." +
					Environment.NewLine + "Arbitrage from " + FM + " to " + TM + " would be ";
				response = Arbitrage >= 0 ? response + "a gain of " + Arbitrage + "%."
					: response + "a lose of " + Arbitrage + "%.";
			}
			return response;
		} // DONE
	}
}
