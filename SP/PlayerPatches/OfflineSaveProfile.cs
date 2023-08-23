using Comfort.Common;
using EFT;
using SIT.Core.Coop;
using SIT.Core.Core;
using SIT.Core.Misc;
using SIT.Core.SP.PlayerPatches.Health;
using SIT.Tarkov.Core;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine.Networking.Match;

namespace SIT.Core.SP.PlayerPatches
{
    public class OfflineSaveProfile : ModulePatch
    {
        public static MethodInfo GetMethod()
        {
            foreach (var method in ReflectionHelpers.GetAllMethodsForType(typeof(TarkovApplication)))
            {
                if (method.Name.StartsWith("method") &&
                    method.GetParameters().Length >= 3 &&
                    method.GetParameters()[0].Name == "profileId" &&
                    method.GetParameters()[1].Name == "savageProfile" &&
                    method.GetParameters()[2].Name == "location" &&
                    method.GetParameters().Any(x => x.Name == "result") &&
                    method.GetParameters()[method.GetParameters().Length - 1].Name == "timeHasComeScreenController"
                    )
                {
                    //Logger.Log(BepInEx.Logging.LogLevel.Info, method.Name);
                    return method;
                }
            }
            Logger.Log(BepInEx.Logging.LogLevel.Error, "OfflineSaveProfile::Method is not found!");

            return null;
        }

        protected override MethodBase GetTargetMethod()
        {
            return GetMethod();
        }

        [PatchPrefix]
        public static bool PatchPrefix(string profileId, RaidSettings ____raidSettings, TarkovApplication __instance, Result<ExitStatus, TimeSpan, object> result)
        {
            // Get scav or pmc profile based on IsScav value
            var profile = ____raidSettings.IsScav
                ? __instance.GetClientBackEndSession().ProfileOfPet
                : __instance.GetClientBackEndSession().Profile;

            var currentHealth = HealthListener.Instance.CurrentHealth;
            SaveProfileProgress(result.Value0, profile, currentHealth, ____raidSettings.IsScav);


            var coopGC = CoopGameComponent.GetCoopGameComponent();
            if (coopGC != null)
            {
                UnityEngine.Object.Destroy(coopGC);
            }

            HealthListener.Instance.MyHealthController = null;
            return true;
        }

        public static void SaveProfileProgress(ExitStatus exitStatus, Profile profileData, PlayerHealth currentHealth, bool isPlayerScav)
        {
            // "Disconnecting" from your game in Single Player shouldn't result in losing your gear. This is stupid.
            if (exitStatus == ExitStatus.Left || exitStatus == ExitStatus.MissingInAction)
                exitStatus = ExitStatus.Runner;

            // TODO: Remove uneccessary data
            var clonedProfile = profileData.Clone();
            //clonedProfile.Encyclopedia = null;
            //clonedProfile.Hideout = null;
            //clonedProfile.Notes = null;
            //clonedProfile.RagfairInfo = null;
            //clonedProfile.Skills = null;
            //clonedProfile.TradersInfo = null;
            //clonedProfile.QuestsData = null;
            //clonedProfile.UnlockedRecipeInfo = null;
            //clonedProfile.WishList = null;

            SaveProfileRequest request = new()
            {
                exit = exitStatus.ToString().ToLower(),
                profile = clonedProfile,
                health = currentHealth,
                isPlayerScav = isPlayerScav
            };

            var convertedJson = request.SITToJson();
            //Logger.LogDebug("SaveProfileProgress =====================================================");
            //Logger.LogDebug(convertedJson);
            int retryCount = 0;
            const int maxRetries = 15; // Limit the number of retries
            const int timeoutMs = 20 * 1000; // Delay between retries in milliseconds

            while (true)
            {
                string result = null;

                try
                {
                    Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     SPP attempt #{retryCount + 1} to post JSON...");
                    result = AkiBackendCommunication.Instance.PostJson("/raid/profile/save", convertedJson, timeout: timeoutMs, debug: true);
                }
                catch (Exception e)
                {
                    Logger.LogError($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     Error while posting JSON: {e}");
                }

                // Check if result is null or empty
                if (!string.IsNullOrEmpty(result))
                {
                    Logger.LogDebug($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     SPP post success, return: {result}");
                    break; // If result is not null or empty, exit the loop
                }

                retryCount++;

                if (retryCount >= maxRetries)
                {
                    Logger.LogError($"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss.fff")}:     Maximum retry attempts reached.");
                    break; // If max retries reached, exit the loop
                }
            }
            //_ = AkiBackendCommunication.Instance.PostJsonAsync("/raid/profile/save", convertedJson, timeout: 10 * 1000, debug: false);


            //Request.Instance.PostJson("/raid/profile/save", convertedJson, timeout: 60 * 1000, debug: true);
        }

        public class SaveProfileRequest
        {
            public string exit { get; set; }
            public Profile profile { get; set; }
            public bool isPlayerScav { get; set; }
            public object health { get; set; }
        }
    }
}
