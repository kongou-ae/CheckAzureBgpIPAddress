using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using Azure.ResourceManager.Network;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Network.Models;
using System.Net;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

namespace CheckAzureIPAddress
{

    public static class Function1
    {


        public class Error
        {
            public string Status { get; set; }
            public string Message { get; set; }
        }

        public class Response
        {
            public bool isMatch { get; set; }
            public List<Detail> Details { get; set; }
        }
        public class Detail
        {
            public string CommunityName { get; set; }
            public string CommunityPrefix { get; set; }
        }
        public static bool ValidateIpAddress(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            Match m = Regex.Match(input, @"^(\d+)\.(\d+)\.(\d+)\.(\d+)$");
            if (!m.Success)
            {
                return false;
            }

            return true;
        }

        [FunctionName("check")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
        
            string targetIpAddress = req.Query["ip"];

            // Validate whether an input value is IPv4 address or not
            if (ValidateIpAddress(targetIpAddress) == false)
            {
                Error error = new();
                error.Status = "400";
                error.Message = $"{targetIpAddress} is not IP Address.";
                
                return new BadRequestObjectResult(error)
                {
                    ContentTypes = { "application/json" },
                    StatusCode = 400,
                };

            }

            try
            {
                TokenCredential cred = new DefaultAzureCredential();
                ArmClient client = new ArmClient(cred);

                string subscriptionId = System.Environment.GetEnvironmentVariable("SubscriptionId");

                ResourceIdentifier subscriptionResourceId = SubscriptionResource.CreateResourceIdentifier(subscriptionId);
                SubscriptionResource subscriptionResource = client.GetSubscriptionResource(subscriptionResourceId);

                Response response = new();
                response.isMatch = false;
                response.Details = new System.Collections.Generic.List<Detail>();

                await foreach (BgpServiceCommunity item in subscriptionResource.GetBgpServiceCommunitiesAsync())
                {
                    // Validate only Ipv4
                    BgpCommunity bgpCommunity = item.BgpCommunities[0];

                    foreach (string CommunityPrefix in bgpCommunity.CommunityPrefixes)
                    {
                        IPNetwork ipnetwork = IPNetwork.Parse(CommunityPrefix);
                        IPAddress ipaddress = IPAddress.Parse(targetIpAddress);

                        bool contains = ipnetwork.Contains(ipaddress);
                        if (contains)
                        {
                            Detail detail = new();
                            detail.CommunityName = bgpCommunity.CommunityName;
                            detail.CommunityPrefix = CommunityPrefix;
                            response.isMatch = true;
                            response.Details.Add(detail);
                            break;
                        }
                    }
                }

                return new OkObjectResult(response)
                {
                    ContentTypes = { "application/json" },
                    StatusCode = 200,
                };


            }
            catch (Exception ex)
            {
                log.LogInformation(ex.Message);
                return new BadRequestObjectResult("Error");

            }
        }
    }
}
