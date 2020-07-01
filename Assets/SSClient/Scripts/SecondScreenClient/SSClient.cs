using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using Newtonsoft.Json.Linq;

namespace SecondScreenClient{

class SSClient : IDisposable{

	private static readonly string ACTION_PREFIX = "/dtv/remote-mediaplayer/scene";

#region Members

	// An instance of HttpViceVersa that can send requests and is also listenning to HTTP
	private HttpBridge httpViceVersa;

	// Ginga's most basic HTTP server's URL with root absolute path
	private Uri gingaUrl;

	// The delegate expected to treat ginga actions
	public delegate IEnumerator OnActionReceivedMethod( string relPath, JObject message, Action<JObject> setResponse, Action<Exception> setException);

	// Event callbacks
	private OnActionReceivedMethod OnActionReceived;
	private Action OnServerDisconnected;

	// Connection object
	private Socket connection;

	public bool Connected {

		get{

			return this.connection.Connected;
		}
	}

	public bool Listening {

		get{

			return this.httpViceVersa.Listening;
		}
	}

	public int Port {

		get{

			return this.httpViceVersa.Port;
		}
	}

	public SSClient( Uri gingaHttpLocation, OnActionReceivedMethod onActionReceived = null, Action onServerDisconnected = null){

DebugStream.outPut += "SSClient.SSClient: entrei.\n";

		this.gingaUrl = new Uri(string.Format("http://{0}:{1}/",gingaHttpLocation.Host,gingaHttpLocation.Port));
		this.httpViceVersa = new HttpBridge(this.OnHttpServerRequested);

		this.OnServerDisconnected = onServerDisconnected;
		this.OnActionReceived = onActionReceived;

DebugStream.outPut += "SSClient.SSClient: pre-inicialização.\n";

		// Register request body
		JObject	body = new JObject(

			new JProperty("location",string.Format("http://{0}:{1}/",Array.Find(Dns.GetHostEntry(Dns.GetHostName()).AddressList,i => i.AddressFamily == AddressFamily.InterNetwork),this.httpViceVersa.Port)),
			new JProperty("deviceType","VR"),
			new JProperty("supportedFormats",new JArray(

				new JValue("x-application-ncl360"),
				new JValue("x-application-x3d"),
				new JValue("x-application-aframe")
			)),
			new JProperty("recognizableEvents",new JArray(

				new JValue("selection"),
				new JValue("lookAt"),
				new JValue("lookAway")
			))
		);

DebugStream.outPut += "SSClient.SSClient: registrando\n";

		// Step two: register its server as a client
		var postResp = this.Post("dtv/remote-mediaplayer/",body,new Tuple<string,string>[]{

			Tuple.Create("Content-Type","application/json; charset=utf-8"),
			Tuple.Create("Accept","application/json; charset=utf-8")
		});



// ################################
DebugStream.outPut += "A response da POST request a: " + postResp.RequestUri.ToString() + " HEADERS:\n\n";

foreach( var key in postResp.ResponseHeaders.AllKeys)
DebugStream.outPut += "Key: \"" + key + "\" Value: \"" + postResp.ResponseHeaders[key] + "\"\n";

DebugStream.outPut += "\nBODY:\n\"" + postResp.ResponseBody + "\"\n";
// ################################



		if( postResp.BadParsed )
			throw new Exception("The register response was bad parsed!");

		if( !postResp.ResponseBody.ContainsKey("control") || postResp.ResponseBody.GetValue("control") == null )
			throw new Exception("The register response has no well formed 'control' key!");

DebugStream.outPut += "SSClient.SSClient: passo 2\n";

		// Step three: creates and configures the TCP socket connection
		this.connection = new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp){

			Blocking = true
		};

		if( Array.Find(Dns.GetHostEntry(gingaHttpLocation.Host).AddressList,i => i.AddressFamily == AddressFamily.InterNetwork) == null )
			throw new Exception("Ginga HTTP server hostnome could not be resolved to an IPv4!");

		// Starts a timer coroutine once csharp doesn't have a TimeOut parameter
		var timer = new Thread(() => { Thread.Sleep(60000); if( !this.connection.Connected ) this.connection.Close(); });
		timer.IsBackground = true;
		timer.Start();

		try{

DebugStream.outPut += "SSClient.SSClient: finishing handshake\n";

			this.connection.Connect(
				Array.Find(Dns.GetHostEntry(gingaHttpLocation.Host).AddressList,i => i.AddressFamily == AddressFamily.InterNetwork),
				int.Parse(postResp.ResponseBody.GetValue("control").ToString())
			);

DebugStream.outPut += "SSClient.SSClient: handshake finished\n";

			ThreadStart connectionControlerDaemon = () => {

				try{

					// The server shall never send a single byte, then it blocks until the connection is broken
					while( this.connection.Receive(new byte[1],1,SocketFlags.None) != 0 );

				}catch( SocketException){

					DebugStream.outPut += "\n\nConnection closed!\n";
				}

				// Once disconnected...
				if( this.OnServerDisconnected != null )
					this.OnServerDisconnected();
			};

			// Once connected, the connection controler is launched
			var control = new Thread(connectionControlerDaemon);
			control.IsBackground = true;
			control.Start();

		}catch( SocketException){

			DebugStream.outPut += "\n\nControl connection timed out!\n";
		}
	}

	public void StartListenerCoroutineOn( MonoBehaviour runner){

		runner.StartCoroutine(this.httpViceVersa.ListenerDaemonCoroutine());
	}

	public void Dispose(){

		try{

			this.connection.Shutdown(SocketShutdown.Both);

		}finally{

			this.httpViceVersa.Dispose();
			this.connection.Close();
		}
	}

#endregion

// ################################
// ############# SSDP #############
// ################################

#region SSDP

	// Call with: "" for root devices; null for all devices; Any other input for specific search
	public static async Task<Dictionary<string,Rssdp.DiscoveredSsdpDevice>> SearchForDevicesType( string type){

		if( type == null )
			DebugStream.outPut += "Searching for all devices...\n";

		else if( type == "" )
			DebugStream.outPut += "Searching for root devices...\n";

		else
			DebugStream.outPut += "Searching for \"" + type + "\" devices...\n";

		IEnumerable<Rssdp.DiscoveredSsdpDevice> results = null;

		using( var deviceLocator = new Rssdp.SsdpDeviceLocator() ){

			if( type != null )
				results = await deviceLocator.SearchAsync(type == "" ? Rssdp.Infrastructure.SsdpConstants.UpnpDeviceTypeRootDevice : type);
			else
				results = await deviceLocator.SearchAsync();
		}

		// ################################
		foreach( var device in results)
			DebugStream.outPut += device.ResponseHeaders.ToString() + "\n";

		return results.ToDictionary(dev => dev.Usn.Substring(5,36),dev => dev);
	}

#endregion

// ################################
// ############# HTTP #############
// ################################

#region HTTP

	public IEnumerator OnHttpServerRequested( HttpListenerContext context){

		// ################################
		// Get and parse the input

		HttpBridge.ParsedReceivedRequest parsedRequest = null;

		// Get a parsed input (syncronous)
		var task = Task.Factory.StartNew(() => parsedRequest = new HttpBridge.ParsedReceivedRequest(context.Request));
		yield return new WaitUntil(() => task.IsCompleted);
		task.Dispose();



// ################################################################
DebugStream.outPut += "\nA request de método: " + context.Request.HttpMethod + " e URL: " + context.Request.Url.OriginalString +
" vinda de: " + context.Request.RemoteEndPoint.Address.ToString() + ": HEADERS:\n\n";

foreach( var key in parsedRequest.Headers.AllKeys)
	DebugStream.outPut += "key: \"" + key + "\" Value: \"" + parsedRequest.Headers[key] + "\"\n";

DebugStream.outPut += "\nBODY:\n\"" + parsedRequest.Body + "\"\n";
// ################################################################



		// ################################
		// Process the input

		JObject responseMsg = null;

		// It's a bad request
		if( parsedRequest.BadParsed ){

			responseMsg = new JObject(new JProperty("error","Request body is not UTF-8 encoded JSON (bad parse)."));
			context.Response.StatusCode = (int) HttpStatusCode.BadRequest;

		// It's an action message
		}else if( context.Request.HttpMethod == HttpMethod.Post.ToString() && context.Request.Url.AbsolutePath.StartsWith(ACTION_PREFIX) &&
			context.Request.ContentType == "application/json; charset=utf-8" && context.Request.AcceptTypes.Contains("application/json; charset=utf-8") ){

			// Clean up
			var relPath = context.Request.Url.AbsolutePath.Substring(ACTION_PREFIX.Length);
			var body = parsedRequest.Body;
			parsedRequest = null;

			// Possible exception
			Exception e = null;

			// Call the request correct treatment
			if( this.OnActionReceived != null )
				yield return this.OnActionReceived(relPath,body,value => responseMsg = value,value => e = value);

			if( e == null ){

				if( responseMsg == null )
					responseMsg = new JObject();

				context.Response.StatusCode = (int) HttpStatusCode.OK;

			}else{

				responseMsg = new JObject(new JProperty("error","Action not accepted:\n\n" + e.ToString()));
				context.Response.StatusCode = (int) HttpStatusCode.BadRequest;
			}

		}else{

			responseMsg = new JObject(new JProperty("error","unrecognizable request."));
			context.Response.StatusCode = (int) HttpStatusCode.BadRequest;

			// #####################################################
			DebugStream.outPut += "Recebi requisição inválida...\n";
		}

// #####################################################
DebugStream.outPut += "enviando response\n";

		// ################################
		// Send the output

		var respondAsync = Task.Factory.StartNew(() => {

			try{

				HttpBridge.SendRequestResponse(context.Response,responseMsg,Encoding.UTF8,new Tuple<string,string>[]{

					Tuple.Create("Content-Type","application/json; charset=utf-8")
				});

			}catch( Exception e){

				DebugStream.outPut += "Capturada exceção no envio da resposta:\n\n" + e.ToString() + "\n";
			}
		});

		yield return new WaitUntil(() => respondAsync.IsCompleted);
		respondAsync.Dispose();

// #####################################################
DebugStream.outPut += "response enviada\n";
	}

	public HttpBridge.ParsedPostRequestResponse Post( string absPath, JObject requestBody, Tuple<string,string>[] extraHeaders = null){

		return this.httpViceVersa.Post(new Uri(this.gingaUrl,absPath),requestBody,Encoding.UTF8,Encoding.UTF8,extraHeaders);
	}

#endregion

}

} // SecondScreenClient namespace