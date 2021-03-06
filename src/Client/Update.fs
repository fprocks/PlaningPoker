﻿module Update

open Shared.Domain
open Models
open Elmish
open Feliz
open Feliz.Router



let init isDarkMode =
    let currentUrl = Router.currentUrl ()
    let id =
        match currentUrl with
        | [] -> ""
        | _ -> currentUrl.[0]

    let state = {
        Error = ""
        Theme = if isDarkMode then Dark else Light
        IsLoading = false
        View = 
            if id = "" then
                CreateGameView {| Name = "" |}
            else
                JoinGameView {| Name = ""; Id = id |}
    }
    
    let gameId = Cookies.getGameId ()

    let cmd =
        if id <> "" && gameId <> Some (GameId.create id) then
            Commands.resetWhenGameNotExists id
        else
            Commands.joinGameFromCookiesOrCheckGameExisits ()

    state, cmd


let update (msg:Models.Msg) state =
    Browser.Dom.console.log ($"%A{msg}")
    match state.View, msg with
    | _, Reset ->
        {
            Error = ""
            Theme = state.Theme
            IsLoading = false
            View = CreateGameView {| Name = "" |}
        }, Cmd.batch [ Commands.removeCookies (); Cmd.ofMsg (Navigate [ "" ])]
    | CreateGameView viewState, CreateGame ->
        if (viewState.Name = "") then
            state, Cmd.ofMsg <| OnError "Name is empty!"
        else
            let player = Player.create viewState.Name
            state, Commands.createGame player

    | CreateGameView _, GameCreated (player,gameId,gameState) ->
        let view = InGameView {
            CurrentPlayer = player 
            WebSocket = None
            GameId = gameId
            CurrentGameState = gameState
        }
        let cmds = 
            Cmd.batch [
                Cmd.ofMsg <| ConnectToWebSocket gameId
                Cmd.ofMsg <| SetCookies
                Cmd.ofMsg <| Navigate [ GameId.extract gameId ]
            ]
        { state with View = view }, cmds

    | JoinGameView viewState, JoinGame ->
        if (viewState.Id = "") then
            state, Cmd.ofMsg <| OnError "Id is empty!"
        elif (viewState.Name = "") then
            state, Cmd.ofMsg <| OnError "Name is empty!"
        else
            let player = Player.create viewState.Name
            let gameId = GameId.create viewState.Id
            state, Commands.joinGame gameId player

    | JoinGameView _, GameJoined (player, gameId, gameState) ->
        let view = InGameView {
            CurrentPlayer = player 
            WebSocket = None
            GameId = gameId
            CurrentGameState = gameState
        }
        let cmds = 
            Cmd.batch [
                Cmd.ofMsg <| ConnectToWebSocket gameId
                Cmd.ofMsg <| SetCookies
                Cmd.ofMsg <| Navigate [ GameId.extract gameId ]
            ]
        { state with View = view }, cmds

    // in case of cookie joining!
    | CreateGameView _, GameJoined (player, gameId, gameState) ->
        let view = InGameView {
            CurrentPlayer = player 
            WebSocket = None
            GameId = gameId
            CurrentGameState = gameState
        }
        let cmds = 
            Cmd.batch [
                Cmd.ofMsg <| ConnectToWebSocket gameId
                Cmd.ofMsg <| SetCookies
                Cmd.ofMsg <| Navigate [ GameId.extract gameId ]
            ]
        { state with View = view }, cmds
    

    | CreateGameView viewState, ChangeName name ->
        let newViewState = {| viewState with Name = name |}
        { state with View = CreateGameView newViewState }, Cmd.none
    | JoinGameView viewState, ChangeName name ->
        let newViewState = {| viewState with Name = name |}
        { state with View = JoinGameView newViewState }, Cmd.none
    | JoinGameView viewState, ChangeId id ->
        let newViewState = {| viewState with Id = id |}
        { state with View = JoinGameView newViewState }, Cmd.none

    | InGameView viewState, GameMsg (StartRound) ->
        state, Commands.startRound viewState.GameId viewState.CurrentPlayer

    | InGameView viewState, GameMsg (FinishRound) ->
        state, Commands.finishRound viewState.GameId viewState.CurrentPlayer
        
    | InGameView viewState, GameMsg (LeaveGame playerToLeave) ->
        let cmd =
            let cmds = [
                Commands.leaveGame viewState.GameId viewState.CurrentPlayer playerToLeave
                // Disconnect from the WebSocket, so you don't get any refreshed.
                // also reset the state
                if (viewState.CurrentPlayer = playerToLeave) then
                    Cmd.ofMsg DisconnectWebSocket
                    Cmd.ofMsg Reset
            ]
            Cmd.batch cmds
            
        state, cmd

    | InGameView viewState, GameMsg (EndGame) ->
        state, Commands.endGame viewState.GameId viewState.CurrentPlayer
        
    | InGameView viewState, GameMsg (PlayCard card) ->
        state, Commands.playCard viewState.GameId viewState.CurrentPlayer card
        
    | InGameView viewState, GameMsg _ ->
        state, Cmd.none
    
    | InGameView viewState, LoadState ->
        state, Commands.loadState viewState.GameId
    | InGameView viewState, SetCurrentGameState gameModel ->
        match gameModel with
        | GameEnded gameId -> // game was ended by admin
            let cmds = 
                [
                    Cmd.ofMsg DisconnectWebSocket
                    Cmd.ofMsg Reset
                ] |> Cmd.batch
            state, cmds
        | _ ->
            let newViewState = { viewState with CurrentGameState = gameModel }
            { state with View = InGameView newViewState; Error = "" }, Cmd.none

    // if the Gamestate comes over the web socket and if you not anymore in the game. Nobody cares.
    | _, SetCurrentGameState gameModel ->
        state, Cmd.none

    | InGameView viewState, ConnectToWebSocket gameId ->
        state, Commands.WebSocket.connectWebSocketCmd gameId
    | InGameView viewState, SetWebSocketHandler ws ->
        let newViewState = { viewState with WebSocket = Some ws }
        { state with View = InGameView newViewState },Cmd.none
    | InGameView viewState, DisconnectWebSocket ->
        let cmd = 
            match viewState.WebSocket with
            | None ->
                Cmd.none
            | Some ws ->
                Commands.WebSocket.disconnectWebsocket ws
        state,cmd
    | _, WebSocketDisconnected ->
        state, Cmd.ofMsg Reset
    | _, OnError error ->
        { state with Error = error }, Cmd.none
    | _, ClearError ->
        { state with Error = "" }, Cmd.none
    | _, ToggleTheme ->
        let newTheme = 
            match state.Theme with
            | Dark -> Light
            | Light -> Dark
        { state with Theme = newTheme }, Cmd.none
    | _, IsLoading b ->
        { state with IsLoading = b }, Cmd.none
    | _, Navigate segments ->
        match segments with
        | [ id ] ->
            state, Cmd.ofSub (fun _ -> Router.navigate (segments,[]))
        | _ ->
            state, Cmd.none
    | InGameView viewState, SetCookies ->
        state, Commands.setCookies viewState.GameId viewState.CurrentPlayer
    | CreateGameView cv, UrlChanged currentUrl ->
        match currentUrl with
        | [ id ] ->
            {state with View = JoinGameView {| Name= cv.Name; Id = id |}}, Cmd.none
        | _ ->
            state, Cmd.none

    | JoinGameView jv, UrlChanged currentUrl ->
        match currentUrl with
        | [] ->
            {state with View = CreateGameView {| Name= jv.Name |}}, Cmd.none
        | _ ->
            state, Cmd.none
    | InGameView _, UrlChanged _ ->
        state, Cmd.none
    | _ ->
        let msgStr = $"%A{msg}"
        let stateStr = $"%A{state}"
        Browser.Dom.console.log($"message: {msgStr}")
        Browser.Dom.console.log($"state: {stateStr}")
        let err = $"Invalid Message '{msg}' with current State. Show Consoel for more details!"
        { state with Error = err }, Cmd.none

