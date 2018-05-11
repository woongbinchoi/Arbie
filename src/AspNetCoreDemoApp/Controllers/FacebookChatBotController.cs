using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;
using System.Net.Http;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using AspNetCoreDemoApp.Models;
using MongoDB.Driver;
using MongoDB.Bson;

namespace AspNetCoreDemoApp.Controllers
{
	public class WebhookModel
	{
		[JsonProperty("object")]
		public string _object { get; set; }
		public List<Entry> entry { get; set; }
	}

	public class Entry
	{
		public string id { get; set; }
		public long time { get; set; }
		public List<Messaging> messaging { get; set; }
	}

	public class Messaging
	{
		public Sender sender { get; set; }
		public Recipient recipient { get; set; }
		public long timestamp { get; set; }
		public Message message { get; set; }
		public Postback postback { get; set; }
	}

	public class Postback
	{
		public string payload { get; set; }
	}

	public class Sender
	{
		public string id { get; set; }
	}

	public class Recipient
	{
		public string id { get; set; }
	}

	public class Message
	{
		public Boolean is_echo { get; set; }
		public Postback quick_reply { get; set; }
		public string mid { get; set; }
		public int seq { get; set; }
		public string text { get; set; }
	}

	[Route("webhook/[controller]")]
	public class FacebookChatBotController : ControllerBase
	{
		private readonly string AppSecret;
		private readonly string PageAccessToken;
		private readonly string VerificationToken;
		private CryptoBase CryptoEngine;

		public FacebookChatBotController()
		{
			AppSecret = Environment.GetEnvironmentVariable("APP_SECRET");
			PageAccessToken = Environment.GetEnvironmentVariable("PAGE_ACCESS_TOKEN");
			VerificationToken = Environment.GetEnvironmentVariable("VERIFICATION_TOKEN");
			CryptoEngine = new CryptoBase();
		}	

		// This is only used for authorizing webhook
		[HttpGet]
		public ActionResult Get()
		{
			var query = Request.Query;

			if (query["hub.mode"] == "subscribe" &&
				query["hub.verify_token"] == VerificationToken)
			{
				var retVal = query["hub.challenge"];
				return Content(retVal, "application/json");
			}
			else
			{
				return NotFound();
			}
		}


		[HttpPost]
		public async Task<HttpResponseMessage> Post([FromBody] WebhookModel value)
		{
			var signature = Request.Headers["X-Hub-Signature"].FirstOrDefault().Replace("sha1=", "");

			if (value._object == "page")
			{
				foreach (var entryelem in value.entry)
				{
					foreach (var item in entryelem.messaging)
					{
						if (item.message != null && item.message.is_echo)
						{
							continue;
						}

						if (item.message == null && item.postback == null)
						{
							continue;
						}
						else
						{
							int timeout = 20000; // 20 seconds
							var task = ProcessPostItem(item);
							if (await Task.WhenAny(task, Task.Delay(timeout)) != task)
							{
								Console.WriteLine("task timeout");
								JObject json = GetMessageTemplate(item.sender.id, "TASK TIMEOUT!");
								await SendMessage(json);
							}
						}
					}
				}
			}
			return new HttpResponseMessage(HttpStatusCode.OK);
		}

		private async Task ProcessPostItem(Messaging item)
		{
			if (item.postback != null)
			{
				await ProcessPostback(item);
			}
			else if (item.message != null)
			{
				if (item.message.quick_reply != null)
				{
					await ProcessQuickReply(item);
				}
				else
				{
					await ProcessMessage(item);
				}
			}
		}


		private JObject GetMessageTemplate(string sender, string text)
		{
			return JObject.FromObject(new
			{
				recipient = new { id = sender },
				message = new { text = text }
			});
		}

		private JObject GetQuickRepliesTemplate(string sender, string msg, string[] buttontext, string[] payload)
		{
			var obj = new
			{
				recipient = new { id = sender },
				message = new
				{
					text = msg,
					quick_replies = new[] {
						new {
							content_type = "text",
							title = buttontext[0],
							payload = payload[0]
						},
						new {
							content_type = "text",
							title = buttontext[1],
							payload = payload[1]
						}
					}
				}
			};
			return JObject.FromObject(obj);
		}

		private JObject GetButtonTemplate(string sender, string msg, string[] buttontext, string[] payload)
		{
			if (buttontext.Length == 3)
			{
				return JObject.FromObject(new
				{
					recipient = new { id = sender },
					message = new {
						attachment = new
						{
							type = "template",
							payload = new
							{
								template_type = "button",
								text = msg,
								buttons = new[]
								{
									new {
										type = "postback",
										title = buttontext[0],
										payload = payload[0]
									},
									new {
										type = "postback",
										title = buttontext[1],
										payload = payload[1]
									},
									new {
										type = "postback",
										title = buttontext[2],
										payload = payload[2]
									}
								}
							}
						}
					}
				});
			}
			else if (buttontext.Length == 2)
			{
				return JObject.FromObject(new
				{
					recipient = new { id = sender },
					message = new
					{
						attachment = new
						{
							type = "template",
							payload = new
							{
								template_type = "button",
								text = msg,
								buttons = new[]
								{
									new {
										type = "postback",
										title = buttontext[0],
										payload = payload[0]
									},
									new {
										type = "postback",
										title = buttontext[1],
										payload = payload[1]
									}
								}
							}
						}
					}
				});
			}
			else
			{
				return JObject.FromObject(new
				{
					recipient = new { id = sender },
					message = new
					{
						attachment = new
						{
							type = "template",
							payload = new
							{
								template_type = "button",
								text = msg,
								buttons = new[]
								{
									new {
										type = "postback",
										title = buttontext[0],
										payload = payload[0]
									}
								}
							}
						}
					}
				});
			}
		}

		private async Task SendMessage(JObject json)
		{
			using (HttpClient client = new HttpClient())
			{
				client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				HttpResponseMessage res =
					await client.PostAsync($"https://graph.facebook.com/v2.6/me/messages?access_token={PageAccessToken}",
					new StringContent(json.ToString(), Encoding.UTF8, "application/json"));
			}
		}

		private async Task SendNewQueryTemplate(string senderid)
		{
			string[] btntext = { "Market arbitrage", "Country arbitrage", "I am not sure" };
			string[] btnpayload = { "NewQueryMarket", "NewQueryCountry", "Help" };
			JObject json = GetButtonTemplate(senderid, "Which arbitrage opportunities would you like to query?", btntext, btnpayload);
			await SendMessage(json);
		}

		private async Task SendHelpMessage(string senderid)
		{
			string HelpMessage = "It's okay. The concept of arbitrage may be confusing for the first time." +
					" I provide a real-time information of various crypto-currency arbitrage opportunites. " + Environment.NewLine +
					"With the support of 100+ fiat currencies, 200+ crypto-currencies and 50+ crypto-currency markets, " +
					"you will be able to compare and find all arbitrage opportunities out there in the world." + Environment.NewLine +
					"For more information, please refer to https://arbiechatbot.herokuapp.com/help." + Environment.NewLine + Environment.NewLine +
					"To begin new search, type 'start'.";
			JObject jsondata = GetMessageTemplate(senderid, HelpMessage);
			await SendMessage(jsondata);
		}

		private async Task SendGreetingMessage(string senderid)
		{
			string name = "User";
			using (var client = new HttpClient())
			{
				client.BaseAddress = new Uri("https://graph.facebook.com/v2.6/");
				string url = string.Format("{0}?access_token={1}&fields=first_name", senderid, PageAccessToken);
				HttpResponseMessage response = client.GetAsync(url).Result;
				if (response.IsSuccessStatusCode)
				{
					string products = response.Content.ReadAsStringAsync().Result;
					dynamic ParsedProduct = JObject.Parse(products);
					name = ParsedProduct["first_name"];
				}
			}

			string msg = $"Hello {name}! My name is Arbie. " + Environment.NewLine +
				"I can provide you with various details regarding current arbitrage opportunities." +
				Environment.NewLine + Environment.NewLine + "Let's get started!";
			string[] ButtonMessages = { "Start New Query", "Read Manual" };
			string[] ButtonPayloads = { "NewQuery", "Help" };
			JObject json = GetQuickRepliesTemplate(senderid, msg, ButtonMessages, ButtonPayloads);
			await SendMessage(json);
		}

		private async Task ProcessMessage(Messaging message)
		{
			string senderid = message.sender.id;
			string msg = message.message.text;
			string msgFormatted = msg.ToLower();

			JObject json;
			string lastaction = "";
			switch (msgFormatted)
			{
				case "start":
					await SendNewQueryTemplate(senderid);
					break;
				case "help":
					await SendHelpMessage(senderid);
					break;
				default:
					lastaction = GetLastAction(senderid);
					if (lastaction != "")
					{
						string[] responses = ProcessArbitrageRequest(lastaction, msg);
						foreach(string res in responses)
						{
							json = GetMessageTemplate(senderid, res);
							await SendMessage(json);
						}
						string endMSG = "Did you find the information that you were looking for? " + Environment.NewLine + Environment.NewLine +
							"If you like to start a new query, type 'start'." + Environment.NewLine +
							" If you like to send the same query but with different parameters, " +
							"just type the information in requested format.";
						json = GetMessageTemplate(senderid, endMSG);
						await SendMessage(json);
					}
					else
					{
						json = GetMessageTemplate(senderid, "Type 'start' to initiate query.");
						await SendMessage(json);
					}
					break;
			}
		}

		private string[] ProcessArbitrageRequest(string LastAction, string message)
		{
			string[] args = message.Split(' ');
			int arrlen = args.Length;

			switch (LastAction)
			{
				case "MarketTwoCoins":
					if (arrlen == 4)
						return new string[] { CryptoEngine.CompareCurrTwoMarket(args[0], args[1], args[2], args[3]) };
					else if (arrlen == 3 && args[2].ToLower() == "showall")
						return new string[] { CryptoEngine.ListMarketTwoCoin(args[0], args[1]) };
					else return new string[] { "Request failed. Check your parameters again. " + Environment.NewLine +
							"You have requested a query of market arbitrage between two coins."};
				case "MarketMultiCoins":
					if (arrlen == 2)
						return new string[] { CryptoEngine.ListCurrTwoMarket(args[0], args[1]) };
					else return new string[] { "Request failed. Check your parameters again. " + Environment.NewLine +
							"You have requested a query of market arbitrage of multiple coins." };
				case "CountryTwoCountry":
					if (arrlen == 3 && args[2].ToLower() == "showall")
						return new string[] { CryptoEngine.ListCoinTwoCountry(args[0], args[1]) };
					else if (arrlen == 3)
						return new string[] { CryptoEngine.CompareCurrTwoCountry(args[2], args[0], args[1]) };
					else return new string[] { "Request failed. Check your parameters again. " + Environment.NewLine +
							"You have requested a query of country arbitrage between two coins." };
				case "CountryMultiCountry":
					if (arrlen == 1 && args[0].ToLower() == "showall")
						return CryptoEngine.ListCoinListCountry();
					else if (arrlen == 1)
						return new string[] { CryptoEngine.ListCountryCointoUSD(args[0]) };
					else return new string[] { "Request failed. Check your parameters again. " + Environment.NewLine +
							"You have requested a query of country arbitrage of multiple coins." };
				default:
					return new string[] { "Requested failed. You have not requested any queries yet." };
			}
		}


		private async Task ProcessQuickReply(Messaging message)
		{
			string senderid = message.sender.id;
			string replypayload = message.message.quick_reply.payload;

			switch (replypayload)
			{
				case "NewQuery":
					await SendNewQueryTemplate(senderid);
					break;
				case "Help":
					await SendHelpMessage(senderid);
					break;
				default:
					JObject json = GetMessageTemplate(senderid, "quick reply received, but I dont know what that is");
					await SendMessage(json);
					break;
			}
		}

		private async Task SaveCurrentAction(string senderid, string operation)
		{
			MongoDBContext dbContext = new MongoDBContext();
			UserLogData NewUserQuery = new UserLogData(senderid, operation);
			var result = await dbContext.UserLog.ReplaceOneAsync(
				filter: new BsonDocument("_id", senderid),
				options: new UpdateOptions { IsUpsert = true },
				replacement: NewUserQuery);
		}

		private string GetLastAction(string senderid)
		{
			MongoDBContext dbContext = new MongoDBContext();
			string lastaction;
			try
			{
				lastaction = dbContext.UserLog.Find(m => m.UserID == senderid).Limit(1).ToList().First().LastSearch;
			} catch
			{
				lastaction = "";
			}
			return lastaction;

		}

		private async Task ProcessPostback(Messaging message)
		{
			string senderid = message.sender.id;
			string payload = message.postback.payload;

			JObject json;
			string msg;
			string[] ButtonMessages;
			string[] ButtonPayloads;

			switch (payload)
			{
				case "Greeting":
					await SendGreetingMessage(senderid);
					break;
				case "NewQueryMarket":
					msg = "Find market arbitrage between...";
					ButtonMessages = new string[]{ "Two coins", "Multiple coins" };
					ButtonPayloads = new string[]{ "MarketTwoCoins", "MarketMultiCoins"};
					json = GetButtonTemplate(senderid, msg, ButtonMessages, ButtonPayloads);
					await SendMessage(json);
					//CompareCurrTwoMarket
					//ListCurrTwoMarket
					//ListMarketTwoCoin
					break;
				case "NewQueryCountry":
					msg = "Find country arbitrage between...";
					ButtonMessages = new string[]{ "Two countries", "Multiple countries" };
					ButtonPayloads = new string[]{ "CountryTwoCountry", "CountryMultiCountry" };
					json = GetButtonTemplate(senderid, msg, ButtonMessages, ButtonPayloads);
					await SendMessage(json);
					//CompareCurrTwoCountry
					//ListCoinTwoCountry
					//ListCountryCointoUSD
					//ListCoinListCountry
					break;
				case "MarketTwoCoins":
					await SaveCurrentAction(senderid, payload);
					msg = "Enter TWO coins, followed by TWO markets." +
						"If you like to query lists of markets, type 'showall' after two coins." +
						Environment.NewLine + "Parameter should be 'Coin1 Coin2 Market1 Market2' or 'Coin1 Coin2 showall'." +
						Environment.NewLine + "Ex) 'BTC USD GDAX Kraken'" +
						Environment.NewLine + "Ex) 'BTC JPY showall'";
					json = GetMessageTemplate(senderid, msg);
					await SendMessage(json);
					//CompareCurrTwoMarket -> FromCurr ToCurr FromMarket ToMarket
					//ListMarketTwoCoin -> FromCurr ToCurr 'showall'
					break;
				case "MarketMultiCoins":
					await SaveCurrentAction(senderid, payload);
					msg = "Enter two market names." +
						Environment.NewLine + "Parameter should be 'Market1 Market2'." +
						Environment.NewLine + "Ex) 'Bitfinex Coinbase'" +
						Environment.NewLine + "Ex) 'bittrex okcoin'";
					json = GetMessageTemplate(senderid, msg);
					await SendMessage(json);
					//ListCurrTwoMarket -> FromMarket ToMarket
					break;
				case "CountryTwoCountry":
					await SaveCurrentAction(senderid, payload);
					msg = "Enter two countries in currency code format, followed by a crypto-currency. " +
						"If you like to query lists of crypto-currency, type 'showall' instead." +
						Environment.NewLine + "Parameter should be 'Country1 Country2 Crypto-Currency' or 'Country1 Country2 showall'." +
						Environment.NewLine + "Ex) 'USD KRW BTC'" +
						Environment.NewLine + "Ex) 'JPY USD showall'";
					json = GetMessageTemplate(senderid, msg);
					await SendMessage(json);
					//CompareCurrTwoCountry -> FromCountry ToCountry CryptoCurrency
					//ListCoinTwoCountry -> FromCountry ToCountry 'showall'
					break;
				case "CountryMultiCountry":
					await SaveCurrentAction(senderid, payload);
					msg = "Enter a crypto-currency that you would like to query. If you like to query lists of coins, type 'showall' instead." +
						Environment.NewLine + "Parameter should be 'CryptoCurrency' or 'showall'." +
						Environment.NewLine + "Ex) 'BTC'" +
						Environment.NewLine + "Ex) 'XRP'" +
						Environment.NewLine + "Ex) 'showall'";
					json = GetMessageTemplate(senderid, msg);
					await SendMessage(json);
					//ListCountryCointoUSD -> CryptoCurrency
					//ListCoinListCountry -> 'showall'
					break;
				case "Help":
					await SendHelpMessage(senderid);
					break;
				default:
					json = GetMessageTemplate(senderid, "Received unknown payload");
					await SendMessage(json);
					break;
			}
		}
	}
}