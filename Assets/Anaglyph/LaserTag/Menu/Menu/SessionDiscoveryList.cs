//using NetworkDiscoveryUnity;
//using System.Net;
//using Unity.Netcode;
//using Unity.Netcode.Transports.UTP;
//using UnityEngine;
//using UnityEngine.UI;

//public class SessionDiscoveryList : SuperAwakeBehavior
//{
//	[SerializeField] private GameObject buttonPrefab;
//	[SerializeField] private GameObject infoText;
//	[SerializeField] private Transform buttonContainer;

//	private void OnEnable()
//	{
//		sessionBroadcaster.OnServerFound.AddListener(OnServerFound);
//		sessionBroadcaster.StartClient();
//		Refresh();
//	}

//	private void OnDisable()
//	{
//		sessionBroadcaster.OnServerFound.RemoveListener(OnServerFound);
//		sessionBroadcaster.StopDiscovery();
//		ClearButtons();
//	}

//	public void Refresh()
//	{
//		ClearButtons();

//		if (sessionBroadcaster.IsRunning)
//		{
//			Debug.Log("Scanning for games...");
//			sessionBroadcaster.ClientBroadcast(new DiscoveryBroadcastData());
//		}
//	}

//	public void ClearButtons()
//	{
//		infoText.gameObject.SetActive(true);

//		for (int i = 0; i < buttonContainer.childCount; i++)
//		{
//			// destruction is not immediate, so this works
//			GameObject g = buttonContainer.GetChild(i).gameObject;
//			if (g != infoText)
//			{
//				Destroy(buttonContainer.GetChild(i).gameObject);
//			}
//		}
//	}

//	void OnServerFound(IPEndPoint sender, DiscoveryResponseData response)
//	{
//		infoText.gameObject.SetActive(false);

//		GameObject g = Instantiate(buttonPrefab, buttonContainer);
//		Button newButton = g.GetComponent<Button>();

//		string address = sender.Address.ToString();

//		newButton.GetComponentInChildren<Text>().text = address;

//		newButton.onClick.AddListener(delegate
//		{
//			UnityTransport transport = 
//			(UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

//			transport.SetConnectionData(address, 25001);
//			NetworkManager.Singleton.StartClient();
//		});
//	}


//}
