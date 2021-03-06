﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp.Server;
using UnityEngine.SceneManagement;


namespace WritersFlock
{
    public class ServerManager : MonoBehaviour
    {

        [Header("Server Config")]
        public bool isPlaying = false;

        public List<Player> players = new List<Player>();
        public List<Story> stories = new List<Story>();
        public List<Title> titles = new List<Title>();
        public Player importantPlayer;
        public Story selectedStory;


        //Singleton
        public static ServerManager instance { get; private set; }

        private void Awake ()
        {
            if (instance != null && instance != this)
            {
                // Destroy if another Gamemanager already exists
                Destroy(gameObject);
            }
            else
            {

                // Here we save our singleton S
                instance = this;
                // Furthermore we make sure that we don't destroy between scenes
                DontDestroyOnLoad(gameObject);
            }
        }

        //Server Init
        private void Start ()
        {
            var socketConnection = new WebSocketServer(1024);
            socketConnection.AddWebSocketService<NetworkService>("/");
            socketConnection.Start();
            Debug.Log("Server running...");
        }

        public IEnumerator AddNewPlayer (string playerName, NetworkService network)
        {
            if (GetPlayerByName(playerName) != null)
            {
                if (isPlaying)
                {
                    for (int i = 0; i < players.Count; i++)
                    {
                        if (players[i].name == playerName)
                        {
                            players[i] = new Player(players[i].name, i, players[i].playerAvatar, network);
                            //May want to put data for loading back into the game in the args here
                            var reconnect = new ServerToClientMessage(MessageType.Connect, "Reconnected", null);
                            network.SendMessageToClient(reconnect);
                        }
                    }
                }
                else
                {
                    var failMessage = new ServerToClientMessage(MessageType.Connect, "Error: That name is taken.", null);
                    network.SendMessageToClient(failMessage);
                }
                yield break;
            }

            var player = new Player(playerName, players.Count, null, network);
            players.Add(player);
            Debug.Log("Player " + player.PlayerNumber + " '" + player.name + "' has joined the game!");

            var lobbyManager = FindObjectOfType<LobbyManager>();
            yield return StartCoroutine(lobbyManager.AddNewPlayerToScreen(player));

            List<string> data = new List<string> { player.playerAvatar.name };
            var successMessage = new ServerToClientMessage(MessageType.Connect, "Connected", data);
            if (players.Count == 1)
            {
                player.isHost = true;
                importantPlayer = player;
                successMessage.message.Add("host");
            }
            player.networkService.SendMessageToClient(successMessage);
            yield return null;
        }

        public IEnumerator StartGame (string playerName)
        {
            //Load main scene and set server to be playing
            SceneManager.LoadScene(1);
            isPlaying = true;
            yield return null;
        }

        public IEnumerator RecievedEntry (string playerName, string message)
        {
            var manager = FindObjectOfType<MainManager>();
            var player = GetPlayerByName(playerName);

            if (manager.CurrentRound().importantPlayerStarts && manager.currentTurn == 0)
            {
                for (int i = 0; i < stories.Count; i++)
                {
                    stories[i].sentances.Add(message);
                }
                if(manager.CurrentRound().roundNumber == 2)
                {
                    List<string> data = new List<string>() { manager.CurrentRound().roundNumber.ToString() };
                    foreach (Player p in players)
                    {
                        p.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Ready, "Starting Round " + manager.CurrentRound().roundNumber, data));
                    }
                }
                ContinueWritingRound(manager);
                yield break;
            }
            else
            {
                switch (manager.CurrentRound().roundNumber)
                {
                    case 1:
                        player.currentStory.sentances.Add(message);
                        player.playerSentances.Add(message);
                        break;
                    case 2:
                        player.currentStory.sentances.Insert(player.currentStory.sentances.Count - 1, message);
                        player.playerSentances.Add(message);
                        break;
                    case 3:
                        var title = new Title(message);
                        titles.Add(title);
                        player.playerTitle = title.titleText;
                        break;
                }
                player.isReady = true;
            }

            if (AllPlayersAreReady())
            {
                ContinueWritingRound(manager);
            }
            yield return null;
        }

        public IEnumerator RecievedVote (string playerName, string message)
        {
            var manager = FindObjectOfType<MainManager>();
            var player = GetPlayerByName(playerName);

            player.isReady = true;

            if (manager.CurrentRound().roundNumber == 1)
            {
                if(manager.currentTurn < stories.Count)
                {
                    for (int i = 0; i < players.Count; i++)
                    {
                        if (players[i].playerSentances.Contains(message))
                        {
                            players[i].points++;
                            break;
                        }
                    }
                    if (AllPlayersAreReady())
                    {
                        ContinueVoting(manager);
                        yield break;
                    }
                }
                else
                {
                    if (AllPlayersAreReady())
                    {
                        NextRound(manager);
                        yield break;
                    }
                }
            }
            else if (manager.CurrentRound().roundNumber == 2)
            {
                foreach(Story story in stories)
                {
                    if(story.title == message)
                    {
                        story.points++;
                        break;
                    }
                }
                if (AllPlayersAreReady())
                {
                    int points = 0;
                    foreach(Story story in stories)
                    {
                        if(story.points > points)
                        {
                            selectedStory = story;
                            points = selectedStory.points;
                        }
                    }
                    NextRound(manager);
                    yield break;
                }
            }
            else
            {
                foreach(Title title in titles)
                {
                    if(title.titleText == message)
                    {
                        title.points++;
                        break;
                    }
                }
                if (AllPlayersAreReady())
                {
                    int points = 0;
                    foreach (Title title in titles)
                    {
                        if (title.points > points)
                        {
                            selectedStory.title = title.titleText;
                            points = title.points;
                        }
                    }
                    NextRound(manager);
                    yield break;
                }
            }
            yield return null;
        }

        public void NextRound (MainManager manager)
        {
            foreach (Player player in players)
            {
                player.isReady = false;
            }
            if (manager.roundIndex >= manager.rounds.Count - 1)
            {
                ShowResults();
                return;
            }
            else
            {
                manager.roundIndex++;
                manager.ChangeToWritingPanel();
                StartWritingRound(manager);
            }
        }

        public void ShowResults ()
        {
            foreach (Player player in players)
            {
                if (player.isHost)
                {
                    player.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Quit, "Play Again?", null));
                }
                else
                {
                    player.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Wait, "The results are in!", null));
                }
            }
            Debug.Log("Show those Results bany");
            //Switch back to the lobby scene and display the results with the options to replay or start a fresh game
            SceneManager.LoadScene(0);
        }

        public IEnumerator ClearGameData (bool restart)
        {
            stories = new List<Story>();
            titles = new List<Title>();
            selectedStory = null;
            if (restart)
            {
                foreach (Player player in players)
                {
                    if (player.isHost)
                    {
                        importantPlayer = player;
                    }
                }
                StartCoroutine(StartGame(importantPlayer.name));
            }
            else
            {
                players = new List<Player>();
                importantPlayer = null;
                isPlaying = false;
                FindObjectOfType<LobbyManager>().CheckLobbyScreenState();
            }
            yield return null;
        }

        public void StartWritingRound (MainManager manager)
        {


            if (stories == null || stories.Count == 0)
            {
                GenerateStories("Once upon a time...", manager);
            }
            //Starts of Round 1 and 2
            if (manager.CurrentRound().importantPlayerStarts)
            {
                if(manager.CurrentRound().roundNumber == 2)
                {
                    int points = 0;
                    foreach (Player player in players)
                    {
                        if (player.points > points)
                        {
                            importantPlayer = player;
                            points = player.points;
                        }
                    }
                }
                importantPlayer.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Wait, "", null, manager.CurrentRound().numberOfWritingTurns));
                importantPlayer.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Entry, manager.CurrentRound().importantPlayerPrompt, importantPlayer.currentStory.sentances));
                foreach(Player player in players)
                {
                    if(importantPlayer == player) { continue; }
                    if(manager.CurrentRound().roundNumber == 1)
                        player.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Wait, "Wait for someone to write the start of the stories!", null, manager.CurrentRound().numberOfWritingTurns));
                    else if(manager.CurrentRound().roundNumber == 2)
                        player.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Wait, "Wait for someone to write the ending!", null, manager.CurrentRound().numberOfWritingTurns));
                }
            }
            //Final Round
            else
            {
                foreach (Player player in players)
                {
                    player.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Entry, "Enter a title for the story!", null));
                }
            }
        }

        public void ContinueWritingRound (MainManager manager)
        {
            foreach(Player player in players)
            {
                player.isReady = false;
            }
            //Move onto voting phase here
            if (manager.currentTurn >= manager.CurrentRound().numberOfWritingTurns)
            {
                Debug.Log("Time to vote!");
                StartVoting(manager);
                return;
            }
            //Keep asking for sentences
            else
            {
                manager.currentTurn++;
                Debug.Log("Moving on to the next turn!");
                CycleStoriesBetweenPlayers(manager);
                foreach (Player player in players)
                {
                    player.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Entry, "Enter the next line in the story!", player.currentStory.sentances));
                }
            }
        }

        public void StartVoting (MainManager manager)
        {
            switch (manager.CurrentRound().roundNumber)
            {
                case 1:
                    manager.ChangeToSingleVotingPanel();
                    break;
                case 2:
                    manager.ChangeToGroupVotingPanel(stories);
                    break;
                case 3:
                    manager.ChangeToGroupVotingPanel(titles);
                    break;
            }
            ContinueVoting(manager);
        }

        public void ContinueVoting (MainManager manager)
        {
            switch (manager.CurrentRound().roundNumber)
            {
                case 1:
                    string body = "";
                    var sentances = stories[manager.currentTurn].sentances;
                    for (int i = 0; i < sentances.Count; i++)
                    {
                        body += sentances[i] + " ";
                    }
                    manager.AdvanceSingleVotingPanel(stories[manager.currentTurn].title, body);
                    break;

            }

            foreach (Player player in players)
            {
                player.isReady = false;
                List<string> data = new List<string>();
                switch (manager.CurrentRound().roundNumber)
                {
                    case 1:
                        data = stories[manager.currentTurn].sentances;
                        break;
                    case 2:
                        foreach(Story story in stories)
                        {
                            data.Add(story.title);
                        }
                        break;
                    case 3:
                        foreach (Title title in titles)
                        {
                            data.Add(title.titleText);
                        }
                        break;
                }

                string message = "";
                switch (manager.CurrentRound().roundNumber)
                {
                    case 1:
                        message = "Select your favorite sentence from this story!";
                        break;
                    case 2:
                        message = "Select your favorite story!";
                        break;
                    case 3:
                        message = "Select your favorite title!";
                        break;
                }

                player.networkService.SendMessageToClient(new ServerToClientMessage(MessageType.Vote, message, data));
            }
            manager.currentTurn++;
        }

        private void GenerateStories (string startingLine, MainManager manager)
        {
            stories = new List<Story>();
            for (int i = 0; i < players.Count; i++)
            {
                Story story = new Story();
                story.title = "Story " + (i + 1);
                story.sentances.Add(startingLine);
                stories.Add(story);
            }
            CycleStoriesBetweenPlayers(manager);
        }

        private void CycleStoriesBetweenPlayers (MainManager manager)
        {
            int start = manager.currentTurn;
            foreach (Player player in players)
            {
                start = start % stories.Count; 
                player.currentStory = stories[start];
                start++;
            }
        }

        private bool AllPlayersAreReady ()
        {
            bool allReady = true;
            foreach (Player player in players)
            {
                if (!player.isReady)
                {
                    allReady = false;
                    break;
                }
            }
            return allReady;
        }

        private Player GetPlayerByName (string name)
        {
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].name == name) { return players[i]; }
            }
            return null;
        }
    }

    [Serializable]
    public class Player
    {
        public Player (string name, int index, Sprite avatar, NetworkService network)
        {
            this.name = name;
            this.playerIndex = index;
            this.playerAvatar = avatar;
            this.networkService = network;
            this.isHost = false;
        }

        public string name;
        public int playerIndex;
        public Sprite playerAvatar;
        public NetworkService networkService;
        public bool isHost;
        public Story currentStory;
        public bool isReady;
        public List<string> playerSentances = new List<string>();
        public int points = 0;
        public string playerTitle;
        public int PlayerNumber
        {
            get
            {
                return playerIndex + 1;
            }
        }
    }
}


