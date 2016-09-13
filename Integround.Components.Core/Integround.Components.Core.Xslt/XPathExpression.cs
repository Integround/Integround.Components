using System.Threading.Tasks;

namespace Integround.Components.Core.Xslt
{
    public class XPathExpression
    {
        public static async Task<T> EvaluateAsync<T>(string xPathExpression, params Message[] msgs)
        {
            var inputXpathDoc = await InputMessageHelper.CreateXPathDocumentAsync(msgs);
            var navigator = inputXpathDoc.CreateNavigator();

            return (T)navigator.Evaluate(xPathExpression);
        }
    }
}
