using System.Threading.Tasks;
using API.Dtos;

namespace API.Services.Interfaces
{
    public interface IInvoicingSignerService
    {
        Task<string> InvoicingSigner(InvoicingSignerDto invoicingSignerDto);
    }
}