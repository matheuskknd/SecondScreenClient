using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

using Newtonsoft.Json.Linq;

// DebugStream
namespace SecondScreenClient{

	public class DebugStream : object{

		private static string _outPut = "";

		public static string outPut {

			get{

				lock(_outPut){

					var tmp = _outPut;
					_outPut = "";

					return tmp;
				}
			}

			set{

				lock(_outPut){

					_outPut = _outPut + value;
				}
			}
		}
	}
}

namespace SecondScreenClient.Sample{

public class CanvasMain : MonoBehaviour{

	[SerializeField]
	private Text outPut = null;

	[SerializeField]
	private Text inPut = null;

	Func<IEnumerator> menuToProcess;
	SSClient ssclient = null;

	// Tells if it's on a async operation
	public bool Blocked { get; private set; } = false;

	private void ShowMenuOptions(){

		this.outPut.text += "\nRSSDP Samples\n";
		this.outPut.text += "Commands\n";
		this.outPut.text += "--------------------------------\n";
		this.outPut.text += "? to display menu\n";
		this.outPut.text += "R to search for root devices\n";
		this.outPut.text += "A to search for all devices\n";
		this.outPut.text += "G to search for Ginga devices\n";
		this.outPut.text += "I interact to Ginga and closes\n";
		this.outPut.text += "X to exit\n\n";
		this.outPut.text += "Command: ";
	}

	private void ShowGingaOptions(){

		this.outPut.text += "\nInterface de envio de eventos!\n";
		this.outPut.text += "Commands\n";
		this.outPut.text += "--------------------------------\n";
		this.outPut.text += "? to display menu\n";
		this.outPut.text += "E send an event\n";
		this.outPut.text += "X to exit\n";
		this.outPut.text += "Command: ";
	}

	// Start is called before the first frame update
	void Start(){

		// Clean the starting output
		this.outPut.text = "";

		// Set the default input treatment
		this.menuToProcess = this.ProcessMainMenu;
		this.ShowMenuOptions();
	}

	public void OnClicked(){

		if( !this.Blocked ){

			this.outPut.text += this.inPut.text + "\n\n";
			this.StartCoroutine(this.menuToProcess());
		}
	}

	private void Update(){

		this.outPut.text += DebugStream.outPut;
	}

	private IEnumerator ProcessMainMenu(){

		this.Blocked = true;

		switch( this.inPut.text.ToUpperInvariant() ){

			case "A":
				var task1 = SSClient.SearchForDevicesType(null);
				yield return new WaitUntil(() => task1.IsCompleted);
				break;

			case "R":
				var task2 = SSClient.SearchForDevicesType("");
				yield return new WaitUntil(() => task2.IsCompleted);
				break;

			case "G":
				var task3 = SSClient.SearchForDevicesType("urn:sbtvd-org:service:GingaCCWebServices:1");
				yield return new WaitUntil(() => task3.IsCompleted);
				break;

			case "I":
				yield return this.ConnectToGinga();

				if( this.ssclient != null ){

					// Change input treatment
					this.menuToProcess = this.ProcessGingaOptions;
					this.ShowGingaOptions();

					this.Blocked = false;
					yield break;
				}

				break;

			case "?":
				break;

			case "X":
				Application.Quit(1);
				break;

			default:
				this.outPut.text += "Unknown command. Press ? for a list of valid commands.\n";
				break;
		}

		this.ShowMenuOptions();
		this.Blocked = false;
	}

	private IEnumerator ProcessGingaOptions(){

		this.Blocked = true;

		switch( this.inPut.text.ToUpperInvariant() ){

			case "E":

				var sendAsync = Task.Factory.StartNew(() => {

					try{

						string eventDestination = "dtv/current-service/apps/appid/nodes/document-id/node-id/";
						var body = new JObject(new JProperty("message","Some event message."));

						var postResp = this.ssclient.Post(eventDestination,body,new Tuple<string,string>[]{

							Tuple.Create("Content-Type","application/json; charset=utf-8"),
							Tuple.Create("Accept","application/json; charset=utf-8")
						});

						DebugStream.outPut += "A response da POST request a: " + postResp.RequestUri + " HEADERS:\n\n";

						foreach( var _key in postResp.ResponseHeaders.AllKeys)
							DebugStream.outPut += "Key: \"" + _key + "\" Value: \"" + postResp.ResponseHeaders[_key] + "\"\n";

						DebugStream.outPut += "\nBODY:\n\"" + postResp.ResponseBody + "\"\n";

					}catch( Exception e){

						DebugStream.outPut += "\nError sending event to Ginga:\n\n" + e.ToString()+ "\n";
					}
				});

				yield return new WaitUntil(() => sendAsync.IsCompleted);
				sendAsync.Dispose();
				break;

			case "X":
				this.OnApplicationQuit();

				// Change input treatment
				this.menuToProcess = this.ProcessMainMenu;
				this.ShowMenuOptions();

				this.Blocked = false;
				yield break;

			case "?":
				break;

			default:
				this.outPut.text += "Unknown command. Press ? for a list of valid commands.\n";
				break;
		}

		this.ShowGingaOptions();
		this.Blocked = false;
	}

	// Syncronous
	private IEnumerator ConnectToGinga(){

		// Step one: Discover every Ginga devices on the network with SSDP
		var findGingaDevices = SSClient.SearchForDevicesType("urn:sbtvd-org:service:GingaCCWebServices:1");
		yield return new WaitUntil(() => findGingaDevices.IsCompleted);

		// Clean up
		var gingaDevices = findGingaDevices.Result;
		findGingaDevices.Dispose();
		findGingaDevices = null;

		if( gingaDevices.Count == 0 ){

			DebugStream.outPut += "\nNenhum dispositivo Ginga encontrado!\n";
			yield break;
		}

		// Get a chosen discovered Ginga SSDP device
		Rssdp.DiscoveredSsdpDevice chosenGinga = gingaDevices.Values.First();

		// Clean up
		gingaDevices.Clear();
		gingaDevices = null;

		if( !chosenGinga.ResponseHeaders.Contains("LOCATION") ){

			DebugStream.outPut += "\nGinga SSDP response header has no LOCATION key!\n";
			yield break;
		}

		if( chosenGinga.ResponseHeaders.GetValues("LOCATION").Count() == 0 ){

			DebugStream.outPut += "\nGinga SSDP LOCATION response header has no value!\n";
			yield break;
		}

		var gingaHTTPLocation = new Uri(chosenGinga.ResponseHeaders.GetValues("LOCATION").First());

		var finishHandShake = Task.Factory.StartNew(() => {

			try{

				this.ssclient = new SSClient(gingaHTTPLocation,this.OnActionReceived,this.OnApplicationQuit);

				// Debug
				DebugStream.outPut += "Control connection stablished!\n";
				DebugStream.outPut += "Socket.Connected: " + ssclient.Connected.ToString() + "\n";

			}catch( Exception e){

				DebugStream.outPut += "\nSSClient construction failed: " + e.ToString() + "\n";
				Assert.IsNull(this.ssclient);
			}
		});

		yield return new WaitUntil(() => finishHandShake.IsCompleted);
		finishHandShake.Dispose();

		// If connected, it's also listenning
		if( this.ssclient != null )
			this.ssclient.StartListenerCoroutineOn(this);
	}

	public IEnumerator OnActionReceived( string relPath, JObject message, Action<JObject> setResponse, Action<Exception> setException){

		DebugStream.outPut += "OnActionReceived.relativePath: " + relPath + "\n";

		// It's a scene
		if( relPath == "/" ){

// ################################################################
DebugStream.outPut += "Registrando recebimento de cena!\n";
// ################################################################

			setResponse(new JObject(new JProperty("message","Ok, a cena requistada foi recebida!")));

		// It's an "action"
		}else if( relPath == "/nodes/node-id/" ){

// ################################################################
DebugStream.outPut += "Registrando recebimento de ação!\n";
// ################################################################

			setResponse(new JObject(new JProperty("message","Ok, a ação enviada foi recebida!")));

		// Default treatment
		}else{

			setException(new Exception("Unknown action."));
		}

		yield break;
	}

	public void OnApplicationQuit(){

		try{

			if( this.ssclient != null )
				this.ssclient.Dispose();

		}finally{

			this.ssclient = null;
		}
	}
}

} // SecondScreenClient.Sample namespace