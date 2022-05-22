using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Net.Http;

namespace THATConfAzFunctionApp
{
    public static class MapLocationCoordinate
    {
        #region Credentials
        static string azureMapstUri = "https://atlas.microsoft.com/search/fuzzy/json";
        static string azureMapsKey = "L6A2QcZexjatoejPsjIJ9xLLjozectRQXeLPF5zJ34s";
        #endregion

        #region Class used to deserialize the request
        private class InputRecord
        {
            public class InputRecordData
            {
                public string Address;
            }

            public string RecordId { get; set; }
            public InputRecordData Data { get; set; }
        }

        private class WebApiRequest
        {
            public List<InputRecord> Values { get; set; }
        }
        #endregion

        #region Classes used to serialize the response
        private class OutputRecord
        {
            public class Position
            {
                public string lat;
                public string lon;
            }

            public class EdmGeographPoint
            {
                public EdmGeographPoint(double lat, double lon)
                {
                    Type = "Point";
                    Coordinates = new double[2];
                    Coordinates[0] = lon;
                    Coordinates[1] = lat;
                }

                public string Type;
                public double[] Coordinates { get; set; }
            }

            public class Geography
            {
                public string Type { get; set; }
                public string Score { get; set; }
                public Position Position { get; set; }
            }

            public class OutputRecordData
            {
                public List<Geography> Results { get; set; }
                public EdmGeographPoint MainGeoPoint { get; set; }
            }

            public class OutputRecordMessage
            {
                public string Message { get; set; }
            }

            public string RecordId { get; set; }
            public OutputRecordData Data { get; set; }
            public List<OutputRecordMessage> Errors { get; set; }
            public List<OutputRecordMessage> Warnings { get; set; }
        }

        private class WebApiResponse
        {
            public WebApiResponse()
            {
                this.values = new List<OutputRecord>();
            }

            public List<OutputRecord> values { get; set; }
        }
        #endregion

        [FunctionName("MapLocationCoordinate")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            // Read input, deserialize it and validate it.
            var request = new StreamReader(req.Body).ReadToEnd();
            var data = GetStructuredInput(request);
            if (data == null)
            {
                return new BadRequestObjectResult("The request schema does not match expected schema.");
            }

            // Calculate the response for each value.
            var response = new WebApiResponse();
            foreach (var record in data.Values)
            {
                if (record == null || record.RecordId == null) continue;

                OutputRecord responseRecord = new OutputRecord();
                responseRecord.RecordId = record.RecordId;

                try
                {
                    responseRecord.Data = GetPosition(record.Data).Result;

                    if (responseRecord.Data != null && responseRecord.Data.Results != null && responseRecord.Data.Results.Count > 0)
                    {
                        var firstPoint = responseRecord.Data.Results[0];

                        if (firstPoint.Position != null)
                        {
                            responseRecord.Data.MainGeoPoint = new OutputRecord.EdmGeographPoint(
                                Convert.ToDouble(firstPoint.Position.lat),
                                Convert.ToDouble(firstPoint.Position.lon));
                        }
                    }

                }
                catch (Exception e)
                {
                    // Something bad happened, log the issue.
                    var error = new OutputRecord.OutputRecordMessage
                    {
                        Message = e.Message
                    };

                    responseRecord.Errors = new List<OutputRecord.OutputRecordMessage>
                    {
                        error
                    };
                }
                finally
                {
                    response.values.Add(responseRecord);
                }
            }

            return new OkObjectResult(response);
        }

        private static WebApiRequest GetStructuredInput(string Request)
        {
            var data = JsonConvert.DeserializeObject<WebApiRequest>(Request);
            if (data == null)
            {
                return null;
            }
            return data;
        }

        /// <summary>
        /// Use Azure Maps to find location of an address
        /// </summary>
        /// <param name="address">The address to search for.</param>
        /// <returns>Asynchronous task that returns objects identified in the image. </returns>
        async static Task<OutputRecord.OutputRecordData> GetPosition(InputRecord.InputRecordData inputRecord)
        {
            var result = new OutputRecord.OutputRecordData();

            var uri = azureMapstUri + "?api-version=1.0&subscription-key="+ azureMapsKey + "&query=" + inputRecord.Address;

            try
            {
                using (var client = new HttpClient())
                using (var request = new HttpRequestMessage())
                {
                    request.Method = HttpMethod.Get;
                    request.RequestUri = new Uri(uri);
                    request.Headers.Add("X-ms-client-id", azureMapsKey);

                    var response = await client.SendAsync(request);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    var data = JsonConvert.DeserializeObject<OutputRecord.OutputRecordData>(responseBody);

                    result = data;
                }
            }
            catch
            {
                result = new OutputRecord.OutputRecordData();
            }

            return result;
        }
    }
}
