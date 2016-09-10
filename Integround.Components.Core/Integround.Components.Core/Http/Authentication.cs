namespace Integround.Components.Http
{
    public enum AuthenticationType { None, Basic, ApiToken }

    public class Authentication
    {
        public AuthenticationType Type { get; set; }
        public Credentials Credentials { get; set; }
        public string ApiToken { get; set; }
    }

    public class Credentials
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }
}
