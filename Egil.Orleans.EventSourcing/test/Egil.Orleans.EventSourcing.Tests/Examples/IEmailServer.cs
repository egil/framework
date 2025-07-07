namespace Egil.Orleans.EventSourcing.Examples;

public interface IEmailServer
{
    ValueTask SendEmail(string email, string message);
}
