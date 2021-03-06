﻿using Smod.Events;
using Smod2;
using Smod2.API;
using Smod2.Attributes;
using Smod2.Events;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace RiskyIPCheckerPlugin
{
	[PluginDetails(
		author = "Dankrushen",
		name = "Risky IP Checker",
		description = "An interface to check all player IPs through https://getipintel.net/",
		id = "dankrushen.ip.checker",
		version = "1.2 mod sk7z",
		SmodMajor = 2,
		SmodMinor = 1,
		SmodRevision = 0
		)]
	class RiskyIPChecker : Plugin
	{
		public IPChecker ipcheck;

		public override void OnDisable()
		{
		}

		public override void OnEnable()
		{
			GameObject gameObject = new GameObject();
			gameObject.AddComponent<IPChecker>();

			this.ipcheck = gameObject.GetComponent<IPChecker>();
			this.ipcheck.Init(this);

			UnityEngine.Object.DontDestroyOnLoad(gameObject);

			this.Info("Risky IP Checker has been enabled!");
		}

		public override void Register()
		{
			// Register Events
			this.AddEventHandler(typeof(IEventPlayerJoin), new PlayerJoinHandler(this), Priority.Normal);
			this.AddEventHandler(typeof(IEventRoundStart), new RoundStartHandler(this), Priority.Normal);

			// Register config settings
			this.AddConfig(new Smod2.Config.ConfigSetting("kick_risky_ips", true, Smod2.Config.SettingType.BOOL, true, "Enables/Disables Risky IP Checker (Uses https://getipintel.net/)"));
			this.AddConfig(new Smod2.Config.ConfigSetting("trusted_ips_reset_every", 10, Smod2.Config.SettingType.NUMERIC, true, "The number of rounds until the cached IPs are cleared"));
			this.AddConfig(new Smod2.Config.ConfigSetting("kick_risky_ips_ratelimit", 30, Smod2.Config.SettingType.NUMERIC, true, "The seconds between requests (CHECK https://getipintel.net/#API)"));
			this.AddConfig(new Smod2.Config.ConfigSetting("kick_risky_ips_email", "", Smod2.Config.SettingType.STRING, true, "The email to use in requests to the api"));
			this.AddConfig(new Smod2.Config.ConfigSetting("kick_risky_ips_subdomain", "check", Smod2.Config.SettingType.STRING, true, "The custom subdomain to use for https://getipintel.net/"));
			this.AddConfig(new Smod2.Config.ConfigSetting("kick_risky_ips_at_percent", 95, Smod2.Config.SettingType.NUMERIC, true, "The percentage of suspicion to kick a player"));
			this.AddConfig(new Smod2.Config.ConfigSetting("ban_risky_ips_at_percent", 100, Smod2.Config.SettingType.NUMERIC, true, "The percentage of suspicion to ban a player"));
			this.AddConfig(new Smod2.Config.ConfigSetting("risky_ip_whitelist", new string[] { }, Smod2.Config.SettingType.LIST, true, "A list of IPs to not check (Prevent them from being checked)"));
            this.AddConfig(new Smod2.Config.ConfigSetting("use_risky_country", false, Smod2.Config.SettingType.BOOL, true, ""));
            this.AddConfig(new Smod2.Config.ConfigSetting("only_use_risky_country", false, Smod2.Config.SettingType.BOOL, true, ""));
            this.AddConfig(new Smod2.Config.ConfigSetting("use_white_list_contry", false, Smod2.Config.SettingType.BOOL, true, "If false, blacklist will be applied"));
            this.AddConfig(new Smod2.Config.ConfigSetting("risky_country_whitelist", new string[] { }, Smod2.Config.SettingType.LIST, true, "A list of Country to whitelist, Use Country Code ISO 3166-1 alpha-2, If you do not enter anything, no one can come in."));
            this.AddConfig(new Smod2.Config.ConfigSetting("risky_country_blacklist", new string[] { }, Smod2.Config.SettingType.LIST, true, "A list of Country to blacklist, Use Country Code ISO 3166-1 alpha-2"));

        }
	}

	class IPChecker : NetworkBehaviour
	{
		RiskyIPChecker plugin;

		// IP Risk Checker
		public Dictionary<string, System.Decimal> smIPTrust = new Dictionary<string, System.Decimal>();
        public Dictionary<string, string> smIPCountry = new Dictionary<string, string>();
        public List<string> smIPQueue = new List<string>();
		public int smUpdateIPTrustEvery; // Rounds
		public int smCurUpdateCount; // Rounds
		// end

		public IPChecker(RiskyIPChecker plugin)
		{
			this.Init(plugin);
		}

		public void Init(RiskyIPChecker plugin)
		{
			this.plugin = plugin;

			// IP Risk Checker
			this.smIPTrust = new Dictionary<string, System.Decimal>();
            this.smIPCountry = new Dictionary<string, string>();

            this.smIPQueue = new List<string>();
			this.smUpdateIPTrustEvery = this.plugin.GetConfigInt("trusted_ips_reset_every"); // Rounds
			this.smCurUpdateCount = smUpdateIPTrustEvery; // Rounds
			// end
		}

		public void Check(Player conn)
		{
			this.StartCoroutine(this.CheckIPRisk(conn));
		}

		// ServerMod - IP Risk Checker
		IEnumerator CheckIPRisk(Player conn)
		{
			string email = this.plugin.GetConfigString("kick_risky_ips_email").Trim();
			email = (email.Length > 0 ? "&contact=" + email : "");
			string[] ipSplit = conn.IpAddress.Split(':');
			string playerAddress = ipSplit[ipSplit.Length - 1].Trim();
            


            string expirationTime = System.DateTime.Now.AddSeconds(15).ToString(); // 15 seconds is long enough to get the ratelimit loop started if there's no issue
			string ratelimitEntry = playerAddress + "|" + expirationTime;

			if (playerAddress.ToLower().Equals(playerAddress.ToUpper()))
			{
				if (!playerAddress.Equals("127.0.0.1"))
				{
                    if (!this.smIPTrust.ContainsKey(playerAddress) || !(this.smCurUpdateCount > 0 && this.smIPTrust.TryGetValue(playerAddress, out System.Decimal percentSure) && this.smIPCountry.TryGetValue(playerAddress, out string country)))
                    {
						if (!this.RatelimitContains(playerAddress))
						{
							bool debugRatelimit = false; // Additional debug output for working on the ratelimiter, the regular debug output should be enough in most cases

							this.smIPQueue.Add(ratelimitEntry); // Ratelimiter
							if (debugRatelimit) this.plugin.Debug("Added item to ratelimit.");

							if (debugRatelimit) this.plugin.Debug("Ratelimiting...");
							while (this.smIPQueue.Count > 0 && this.RatelimitContains(playerAddress) && !this.RatelimitEquals(playerAddress, this.smIPQueue[0]))
							{
								// Ratelimit timeout
								string currentRatelimit = this.smIPQueue[0];
								if (this.RatelimitExpired(currentRatelimit))
								{
									while (this.smIPQueue.Contains(currentRatelimit))
									{
										if (debugRatelimit) this.plugin.Debug("Ratelimit timed out...");
										this.smIPQueue.Remove(currentRatelimit); // Ratelimiter
									}
								}
								// Ratelimit timeout end

								yield return new WaitForSeconds(1); // Convenient delay timer

								this.UpdateRatelimitTime(playerAddress, 15); // Reset timeout so it doesn't get autoremoved
							}
							if (debugRatelimit) this.plugin.Debug("Out of ratelimit!");

							if (this.smIPQueue.Count > 0 && this.RatelimitContains(playerAddress) && this.RatelimitEquals(playerAddress, this.smIPQueue[0]))
							{
								yield return this.StartCoroutine(this.FetchIPRisk(conn, playerAddress, email));

								int ratelimitTime = this.plugin.GetConfigInt("kick_risky_ips_ratelimit");
								this.UpdateRatelimitTime(playerAddress, ratelimitTime + 15); // Set timeout with a bit of extra time
								yield return new WaitForSeconds(ratelimitTime); // Ratelimit timer
							}

							foreach (string entry in this.smIPQueue)
							{
								string[] split = entry.Split(new char[] { '|' }, 2);
								if (split.Length == 2)
								{
									if (split[0].Equals(playerAddress))
									{
										if (debugRatelimit) this.plugin.Debug("Ratelimit freed!");
										this.smIPQueue.Remove(entry); // Ratelimiter
									}
								}
								else
								{
									if (debugRatelimit) this.plugin.Debug("Ratelimit freed!");
									this.smIPQueue.Remove(entry); // Ratelimiter
								}
							}
						}
					}
					else
					{
						this.plugin.Debug("Player IP already checked Suspicion: " + percentSure + "% Nick: \"" + conn.Name + "\" IP: " + playerAddress + "\" Country: " + country + "\" SteamID: " + conn.SteamId);

                        this.RiskyIPAction(conn, percentSure, country, playerAddress);
					}
				}
				else
				{
					this.plugin.Debug("Player IP is the local client: " + playerAddress);
				}
			}
			else
			{
				this.plugin.Debug("Player IP has letters, not checking: " + playerAddress);
			}
		}

		public bool RatelimitExpired(string entry)
		{
			string[] split = entry.Split(new char[] { '|' }, 2);
			if (split.Length == 2)
			{
				return System.Convert.ToDateTime(split[1]) < System.DateTime.Now;
			}
			else
			{
				return true;
			}
		}

		public bool RatelimitEquals(string address, string entry)
		{
			string[] split = entry.Split(new char[] { '|' }, 2);
			if (split.Length == 2)
			{
				return split[0].Equals(address);
			}

			return false;
		}

		public bool RatelimitContains(string address)
		{
			foreach (string matchEntry in this.smIPQueue)
			{
				string[] split = matchEntry.Split(new char[] { '|' }, 2);
				if (split.Length == 2 && split[0].Equals(address))
				{
					return true;
				}
			}

			return false;
		}

		public void UpdateRatelimitTime(string address, int timeoutSeconds)
		{
			string expirationTime = System.DateTime.Now.AddSeconds(timeoutSeconds).ToString();

			for (int i = 0; i < this.smIPQueue.Count; i++)
			{
				string[] split = this.smIPQueue[i].Split(new char[] { '|' }, 2);
				if (split.Length == 2 && split[0].Equals(address))
				{
					this.smIPQueue.RemoveAt(i);
					this.smIPQueue.Insert(i, address + "|" + expirationTime);
					return;
				}
			}
		}

		IEnumerator FetchIPRisk(Player conn, string testAddress, string email)
		{
			this.plugin.Debug("Checking player Nick: \"" + conn.Name + "\" IP: " + testAddress + "\" SteamID: " + conn.SteamId);

			string webRequest = "http://" + this.plugin.GetConfigString("kick_risky_ips_subdomain") + ".getipintel.net/check.php?ip=" + testAddress + email + "&flags=f&oflags=c";
            this.plugin.Debug("Contacting website with request: " + webRequest);
			UnityWebRequest www = UnityWebRequest.Get(webRequest);
			yield return www.SendWebRequest();
			if (string.IsNullOrEmpty(www.error) && float.TryParse(www.downloadHandler.text.Split(',')[0], out float likelyBad))
			{

                string country = www.downloadHandler.text.Split(',')[1];
                this.smIPCountry.Add(testAddress, country);

                System.Decimal percentSure = System.Math.Round((System.Decimal)(likelyBad * 100f), 1, System.MidpointRounding.ToEven);
				this.smIPTrust.Add(testAddress, percentSure);


				this.plugin.Debug("Player IP checked Suspicion: " + percentSure + "% Nick: \"" + conn.Name + "\" IP: " + testAddress +"\" Country: " + country +" SteamID: " + conn.SteamId);

				this.RiskyIPAction(conn, percentSure, country, testAddress);
			}
			else
			{
				this.plugin.Debug("Error on player IP: " + testAddress);
				this.plugin.Debug("Error: " + www.error);
				this.plugin.Debug("Response: " + www.downloadHandler.text);

				switch (www.downloadHandler.text)
				{
					case "-1":
						this.plugin.Debug("Invalid query, no input (http://getipintel.net/)");
						break;
					case "-2":
						this.plugin.Debug("Invalid IP address (http://getipintel.net/)");
						break;
					case "-3":
						this.plugin.Debug("Unroutable address / private address (http://getipintel.net/)");
						break;
					case "-4":
						this.plugin.Debug("Unable to reach database, most likely the database is being updated. Keep an eye on twitter for more information. (http://getipintel.net/)");
						break;
					case "-5":
						this.plugin.Debug("Your connecting IP has been banned from the system or you do not have permission to access a particular service. Did you exceed your query limits? Did you use an invalid email address? (http://getipintel.net/)");
						break;
					case "-6":
						this.plugin.Debug("You did not provide any contact information with your query or the contact information is invalid. (http://getipintel.net/)");
						break;
				}
			}
		}

		public void RiskyIPAction(Player conn, System.Decimal percentSure,string country, string testAddress)
		{

            if (this.plugin.GetConfigBool("use_risky_country"))
            {
                bool risky_country;
                if (this.plugin.GetConfigBool("use_white_list_contry"))
                {
                    risky_country = true;
                    string[] whiteList = this.plugin.GetConfigList("risky_country_whitelist");
                    if (whiteList.Length == 0)
                    {
                        plugin.Info("RiskyCountryCheck ignored because nothing is set in the whitelist");
                        risky_country = false;
                    }
                    foreach (string whitelistcountry in whiteList)
                    {
                        if (country == whitelistcountry)
                        {
                            risky_country = false;
                        }
                    }

                }
                else
                {
                    risky_country = false;
                    string[] blackList = this.plugin.GetConfigList("risky_country_blacklist");
                    if(blackList.Length == 0)
                    {
                        plugin.Info("RiskyCountryCheck ignored because nothing is set in the blacklist");
                    }
                    foreach (string blacklistcountry in blackList)
                    {
                        if (country == blacklistcountry)
                        {
                            risky_country = true;
                        }
                    }
                }
                if (risky_country)
                {
                    this.plugin.Info("Banning player for having a risky Country (" + percentSure + "%) Nick: \"" + conn.Name + "\" IP: " + testAddress + "\" Country: " + country + " SteamID: " + conn.SteamId);
                    conn.Ban(26297460);
                }
            }
            if (this.plugin.GetConfigBool("only_use_risky_country") == false)
            {
                if (percentSure >= (System.Decimal)this.plugin.GetConfigInt("ban_risky_ips_at_percent"))
                {
                    this.plugin.Info("Banning player for having a known bad IP (" + percentSure + "%) Nick: \"" + conn.Name + "\" IP: " + testAddress + "\" Country: " + country + " SteamID: " + conn.SteamId);
                    conn.Ban(26297460);
                }
                else if (percentSure >= (System.Decimal)this.plugin.GetConfigInt("kick_risky_ips_at_percent"))
                {
                    this.plugin.Info("Kicking player for having a suspicious IP (" + percentSure + "%) Nick: \"" + conn.Name + "\" IP: " + testAddress + "\" Country: " + country + " SteamID: " + conn.SteamId);
                }
            }
		}
		// ServerMod - IP Risk Checker end
	}
}
