using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace API.Controllers
{
    
    [ApiController]
    [Route("[controller]")]
    public class EInvoicingSignerController : ControllerBase
    {
        private readonly ILogger<EInvoicingSignerController> _logger;


        public EInvoicingSignerController(ILogger<EInvoicingSignerController> logger)
        {
            _logger = logger;
        }

        [HttpGet("GetInvoicingSigner")]
        public async Task<ActionResult<string>> GetInvoicingSigner(string Token) => Ok(Token);
    }
}