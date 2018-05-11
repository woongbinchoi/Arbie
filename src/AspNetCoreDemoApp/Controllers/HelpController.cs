using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AspNetCoreDemoApp.Controllers
{
    [Route("help")]
	public class HelpController : ControllerBase
	{
		[HttpGet]
		public ActionResult Get()
		{
			return File("~/src/" + "helpMessages.html", "text/html");
		}
	}
}