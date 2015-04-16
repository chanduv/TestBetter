using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.WebTesting;
using Newtonsoft.Json;

namespace TestBetter.Plugins
{
    /// <summary>
    /// Class to get values from context or the application configuration file and set the request and sign appropriately
    /// </summary>
    public class RequestHelper : WebTestRequestPlugin
    {
        [DisplayName("VariableRequestBody")]
        [Description("Partial or complete Request Body, usually a json or a subset of a bigger json string")]
        public string VariableRequestBody { get; set; }

        [DisplayName("Content-type")]
        [Description("Content-type")]
        public string ContentType { get; set; }

        [DisplayName("Sign Request")]
        [Description("Is this a personal API which is required to be signed")]
        public bool SignRequest { get; set; }

        private string ApplicationName { get; set; }

        /// <summary>
        /// This mehtod overrides the the request object with custom values for each request made in a webtest.
        /// 1. Gets values from App.config and updates context params
        /// 2. Get values from the context and update the Request Header
        /// 3. Get values from the context and update the Request Body
        /// 4. Sign request and submit
        /// </summary>
        /// <param name="sender">Object</param>
        /// <param name="preRequestEventArgs">PreRequestEventArgs</param>
        public override void PreRequest(object sender, PreRequestEventArgs preRequestEventArgs)
        {
            InitRequest(preRequestEventArgs);
            GenerateQueryParams(preRequestEventArgs);
            UpdateRequestURL(preRequestEventArgs);
            GenerateEmailAddress(preRequestEventArgs);
            GenerateRequestHeaders(preRequestEventArgs);

            if (SignRequest)
            {
                GenerateRequestBody(preRequestEventArgs);
                AuthorizeRequest(preRequestEventArgs);
            }

            preRequestEventArgs.WebTest.InitializeDataBinding();
            base.PreRequest(sender, preRequestEventArgs);
        }

        /// <summary>
        /// Gets values from the application configuration file and updates them to the context parameters and webtest fields
        /// </summary>
        /// <param name="preRequestEventArgs">the webtest event arguments</param>
        private void InitRequest(PreRequestEventArgs preRequestEventArgs)
        {
            var contextParameters = preRequestEventArgs.WebTest.Context.ToDictionary(context => context.Key, context => context.Value);
            var contextKeys = new List<string>(contextParameters.Keys);
            var configFileMap = new ExeConfigurationFileMap { ExeConfigFilename = "App.config" };
            var config = ConfigurationManager.OpenMappedExeConfiguration(configFileMap, ConfigurationUserLevel.None);
            var appSettingsSection = config.GetSection("appSettings") as AppSettingsSection;

            if (appSettingsSection == null) return;

            var appSettings = appSettingsSection.Settings;

            foreach (var contextkey in contextKeys.Where(contextkey => appSettings[contextkey] != null))
            {
                if (string.IsNullOrEmpty(contextParameters[contextkey].ToString()))
                {
                    preRequestEventArgs.WebTest.Context.Add(contextkey, appSettings[contextkey].Value);
                }
            }

            if (appSettings["ApplicationName"] != null)
            {
                ApplicationName = appSettings["ApplicationName"].Value;
            }
            
            if (!contextParameters.ContainsKey("SuccessRequestBody") && !string.IsNullOrEmpty(VariableRequestBody) && !string.IsNullOrWhiteSpace(VariableRequestBody))
            {
                preRequestEventArgs.WebTest.Context.Add("SuccessRequestBody", VariableRequestBody);
            }
        }

        private void UpdateRequestURL(PreRequestEventArgs preRequestEventArgs)
        {
            var requestURL = preRequestEventArgs.Request.Url;
            if (requestURL.Contains("@"))
            {
                requestURL = requestURL.TrimStart('@');
                requestURL = requestURL.Substring(0, requestURL.IndexOf('@'));
                var resource = preRequestEventArgs.Request.Url.Substring(preRequestEventArgs.Request.Url.LastIndexOf('@') + 1);
                preRequestEventArgs.Request.Url = preRequestEventArgs.WebTest.Context[requestURL] + resource;
            }
        }

        /// <summary>
        /// Sign Request, Add headers
        /// </summary>
        /// <param name="preRequestEventArgs"></param>
        private static void AuthorizeRequest(PreRequestEventArgs preRequestEventArgs)
        {
            //Add code to get authentication keys and values and sign the request as specified for your request.
        }

        private static void GenerateRequestHeaders(PreRequestEventArgs preRequestEventArgs)
        {
            var headers = preRequestEventArgs.Request.Headers;

            foreach (var header in headers)
            {
                if (header.Value.StartsWith("@") && header.Value.EndsWith("@"))
                {
                    var key = header.Value.Trim('@');
                    header.Value = preRequestEventArgs.WebTest.Context[key].ToString();
                }
            }
        }

        private void GenerateRequestBody(PreRequestEventArgs preRequestEventArgs)
        {
            var requestBodyString = string.Empty;

            if (preRequestEventArgs.WebTest.Context.ContainsKey("SuccessRequestBody"))
            {
                var validRequestBodyDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(preRequestEventArgs.WebTest.Context["SuccessRequestBody"].ToString());
                var validRequestBodyKeys = new List<string>(validRequestBodyDictionary.Keys);
                var contextParameters = preRequestEventArgs.WebTest.Context.ToDictionary(context => context.Key, context => context.Value);

                if (!string.Equals(VariableRequestBody, preRequestEventArgs.WebTest.Context.ContainsKey("SuccessRequestBody").ToString()))
                {
                    preRequestEventArgs.WebTest.Context["GenerateEmailAddress"] = FormatEmail();
                }
                foreach (var validRequestBodyKey in validRequestBodyKeys)
                {
                    validRequestBodyDictionary[validRequestBodyKey] = contextParameters[validRequestBodyKey].ToString();
                }

                if (!string.IsNullOrEmpty(VariableRequestBody) && !string.IsNullOrWhiteSpace(VariableRequestBody))
                {
                    var variableRequestBodyDictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(VariableRequestBody);
                    var keys = new List<string>(variableRequestBodyDictionary.Keys);

                    foreach (var key in keys.Where(key => variableRequestBodyDictionary.ContainsKey(key) && variableRequestBodyDictionary[key].StartsWith("{{") && variableRequestBodyDictionary[key].EndsWith("}}")))
                    {
                        variableRequestBodyDictionary[key] = contextParameters[key].ToString();
                    }

                    foreach (var key in keys.Where(key => validRequestBodyDictionary.ContainsKey(key)))
                    {
                        validRequestBodyDictionary[key] = variableRequestBodyDictionary[key];
                    }
                }

                requestBodyString = JsonConvert.SerializeObject(validRequestBodyDictionary);
            }

            var requestBody = new StringHttpBody
            {
                ContentType = ContentType ?? "Application//Json",
                BodyString = requestBodyString
            };

            preRequestEventArgs.Request.Body = requestBody;
        }

        /// <summary>
        /// Generates a random email address for the webtest
        /// </summary>
        /// <param name="preRequestEventArgs">preRequestEventArgs</param>
        private void GenerateEmailAddress(PreRequestEventArgs preRequestEventArgs)
        {
            var contextParameters = preRequestEventArgs.WebTest.Context.ToDictionary(context => context.Key, context => context.Value);
            var contextKeys = new List<string>(contextParameters.Keys);

            foreach (var contextKey in contextKeys.Where(contextKey => contextKey.Equals("@GenerateEmailAddress@") || contextParameters[contextKey].ToString().Equals("@GenerateEmailAddress@")))
            {
                contextParameters[contextKey] = FormatEmail();
            }
        }

        private string FormatEmail()
        {
            return string.Format("WebTest{0}{1}@MySecretBogusEmailServiceProvider.com", Math.Floor(DateTime.Now.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds).ToString(CultureInfo.InvariantCulture), ApplicationName);
        }

        private void GenerateQueryParams(PreRequestEventArgs preRequestEventArgs)
        {
            var queryParams = preRequestEventArgs.Request.QueryStringParameters;
            foreach (var queryParam in queryParams)
            {
                if (queryParam.Value.StartsWith("@") && queryParam.Value.EndsWith("@"))
                {
                    var key = queryParam.Value.Trim('@');
                    queryParam.Value = preRequestEventArgs.WebTest.Context[key].ToString();
                }
            }
        }
    }
}
