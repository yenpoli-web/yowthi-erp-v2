namespace YowThi.Erp.Api.Localization;

public interface IApiLocaleResolver
{
    string Resolve(HttpRequest request);
}
