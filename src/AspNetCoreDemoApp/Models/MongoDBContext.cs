using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AspNetCoreDemoApp.Models
{
	public class UserLogData
	{
		[BsonId]
		public string UserID { get; set; }
		[BsonElement("last_search")]
		public string LastSearch { get; set; }

		public UserLogData(string id, string newq)
		{
			UserID = id;
			LastSearch = newq;
		}
	}

	public class MongoDBContext
	{
		public static string ConnectionString { get; set; }
		public static string DatabaseName { get; set; }

		private IMongoDatabase _database { get; }

		public MongoDBContext()
		{
			try
			{
				ConnectionString = Environment.GetEnvironmentVariable("MONGODB_URI");
				DatabaseName = Environment.GetEnvironmentVariable("DATABASE_NAME");
				MongoClientSettings settings = MongoClientSettings.FromUrl(new MongoUrl(ConnectionString));
				var mongoClient = new MongoClient(settings);
				_database = mongoClient.GetDatabase(DatabaseName);
			}
			catch (Exception ex)
			{
				throw new Exception("Can not access to db server.", ex);
			}
		}

		public IMongoCollection<UserLogData> UserLog
		{
			get
			{
				return _database.GetCollection<UserLogData>("UserLog");
			}
		}
	}
}
