using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API;
using API.Dtos;
using API.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace API.Controllers
{
    
    [ApiController]
    [Route("[controller]")]
    public class EInvoicingSignerController : ControllerBase
    {
        private readonly IInvoicingSignerService _invoicingSignerService;
        private readonly ILogger<EInvoicingSignerController> _logger;


        public EInvoicingSignerController(IInvoicingSignerService invoicingSignerService )
        {
            _invoicingSignerService = invoicingSignerService;
        }

        [HttpPost("InvoicingSigner")]
        public async Task<ActionResult> InvoicingSigner(InvoicingSignerDto invoicingSignerDto) => Ok( await _invoicingSignerService.InvoicingSigner(invoicingSignerDto));
    }
}