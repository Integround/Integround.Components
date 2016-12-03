using System.Threading.Tasks;
using Integround.Components.Core;

namespace Integround.Components.Xml.Xslt
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
