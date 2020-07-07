using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Drawing;
using System.Linq;
using SnakeBattle.Api;

namespace SnakeBattle.Logic
{
    partial class GameBoard
    {
        static private partial class SnakesCalculator
        {
            static public void CalculateSnakes(
                IReadOnlyDictionary<BoardElement, ImmutableHashSet<PointInfo>> boardElementToInfosIndex,
                IReadOnlyDictionary<Point, PointInfo> locationToPointInfoIndex,
                IReadOnlyList<ISnakeInfo> previousTickSnakeInfos,
                IReadOnlyDictionary<Point, PointInfo> previousTickLocationToPointInfoIndex,
                Dictionary<Point, ISnakeInfo> locationToSnakeInfoIndex,
                out IReadOnlyDictionary<Point, ISnakeInfo> snakeHeadToSnakeInfoIndex)
            {
                var snakeInfos = new List<SnakeInfo>();
                SnakeInfo playerSnake = CalculatePlayerSnake(
                    locationToPointInfoIndex,
                    boardElementToInfosIndex,
                    previousTickSnakeInfos,
                    previousTickLocationToPointInfoIndex
                );

                snakeInfos.Add(playerSnake);

                CalculateEnemySnakes(
                    boardElementToInfosIndex,
                    locationToPointInfoIndex,
                    previousTickSnakeInfos,
                    previousTickLocationToPointInfoIndex,
                    snakeInfos
                );

                snakeHeadToSnakeInfoIndex = snakeInfos.ToDictionary(x => x.Head, x => (ISnakeInfo) x);
                foreach (SnakeInfo snakeInfo in snakeInfos)
                {
                    foreach (Point bodyPoint in snakeInfo.BodyPoints)
                    {
                        locationToSnakeInfoIndex[bodyPoint] = snakeInfo;
                    }
                }
            }

            static private SnakeInfo CalculatePlayerSnake(
                IReadOnlyDictionary<Point, PointInfo> locationToPointInfoIndex,
                IReadOnlyDictionary<BoardElement, ImmutableHashSet<PointInfo>> boardElementToInfosIndex,
                IReadOnlyList<ISnakeInfo> previousTickSnakeInfos,
                IReadOnlyDictionary<Point, PointInfo> previousTickLocationToPointInfoIndex)
            {
                SnakeInfo playerSnake = default;
                ImmutableHashSet<PointInfo> playerHeads = GetPlayerHeads(boardElementToInfosIndex);
                PointInfo head = playerHeads?.Single();
                if (head != default)
                {
                    playerSnake = new SnakeInfo
                    {
                        FoundHead = true,
                        FuryTicksLeft = head.BoardElement == BoardElement.HeadEvil ? -1 : 0,
                        IsActive =
                            head.BoardElement != BoardElement.HeadSleep &&
                            head.BoardElement != BoardElement.HeadDead,
                        IsPlayerSnake = true
                    };

                    playerSnake.AddHead(head.Location);
                }
                else
                {
                    ImmutableHashSet<PointInfo> playerTails = GetPlayerTails(boardElementToInfosIndex);
                    PointInfo tail = playerTails?.SingleOrDefault();
                    if (tail != default)
                    {
                        playerSnake = new SnakeInfo
                        {
                            FoundTail = true,
                            FuryTicksLeft = -2,
                            IsActive = tail.BoardElement != BoardElement.TailInactive,
                            IsPlayerSnake = true
                        };

                        playerSnake.AddTail(tail.Location);
                    }
                }

                if (playerSnake == default)
                {
                    playerSnake = new SnakeInfo
                    {
                        FoundHead = false,
                        FoundTail = false,
                        IsPlayerSnake = true,
                        FuryTicksLeft = -2,
                        // Неактивного тела не бывает.
                        IsActive = true
                    };

                    playerSnake.AddTail(GetPlayerBodyPart(boardElementToInfosIndex).First().Location);
                }

                bool foundNext = true;
                while (foundNext)
                {
                    foundNext = false;

                    if (playerSnake.FoundHead)
                    {
                        PointInfo currentTail = locationToPointInfoIndex[playerSnake.Tail];
                        if (TryGetNextPlayerPoint(playerSnake, currentTail, out PointInfo newTail, searchForBody: true))
                        {
                            playerSnake.AddTail(newTail.Location);
                            foundNext = true;
                        }
                        else if (TryGetNextPlayerPoint(playerSnake, currentTail, out newTail, searchForTail: true))
                        {
                            playerSnake.AddTail(newTail.Location);
                            playerSnake.FoundTail = true;
                            // Если найдены хвост и голова, значит найдено всё тело.
                        }
                    }
                    else if (playerSnake.FoundTail)
                    {
                        PointInfo currentHead = locationToPointInfoIndex[playerSnake.Head];
                        if (TryGetNextPlayerPoint(playerSnake, currentHead, out PointInfo newHead, searchForBody: true))
                        {
                            playerSnake.AddHead(newHead.Location);
                            foundNext = true;
                        }

                        // Здесь искать голову не надо, т.к. иначе было бы foundHead == true.
                    }
                    else
                    {
                        PointInfo currentHead = locationToPointInfoIndex[playerSnake.Head];
                        bool gotNewHead = TryGetNextPlayerPoint(playerSnake, currentHead, out PointInfo newHead, searchForBody: true);
                        if (gotNewHead)
                        {
                            playerSnake.AddHead(newHead.Location);
                            foundNext = true;
                        }

                        PointInfo currentTail = locationToPointInfoIndex[playerSnake.Tail];
                        bool gotNewTail = TryGetNextPlayerPoint(playerSnake, currentTail, out PointInfo newTail, searchForBody: true);
                        if (gotNewTail)
                        {
                            playerSnake.AddTail(newTail.Location);
                            foundNext = true;
                        }
                    }
                }

                ISnakeInfo previousStateSnake = previousTickSnakeInfos?.First(x => x.IsPlayerSnake);

                bool checkReverse =
                    !playerSnake.FoundHead &&
                    !playerSnake.FoundTail;

                if (checkReverse && previousStateSnake != null)
                {
                    Point previousTail = previousStateSnake.Tail;

                    // Когда змейка уползает, её хвост сдвигается. Таким образом, если текущее тело содержит старый хвост в позиции 1, то это не хвост, а голова.
                    if (playerSnake.Neck == previousTail)
                    {
                        playerSnake.ReverseBody();
                    }
                }

                bool checkFuryState =
                    playerSnake.FuryTicksLeft < 0;

                if (checkFuryState)
                {
                    if (previousStateSnake != null)
                    {
                        playerSnake.FuryTicksLeft = Math.Max(previousStateSnake.FuryTicksLeft - 1, 0);
                    }
                    else
                    {
                        playerSnake.FuryTicksLeft = Consts.FuryDuration;
                    }

                    if (previousTickLocationToPointInfoIndex?[playerSnake.Head].BoardElement == BoardElement.FuryPill)
                    {
                        playerSnake.FuryTicksLeft += Consts.FuryDuration;
                    }
                }

                if (previousStateSnake != null)
                {
                    playerSnake.Stones = previousStateSnake.Stones;
                }

                if (previousTickLocationToPointInfoIndex?[playerSnake.Head].BoardElement == BoardElement.Stone)
                {
                    playerSnake.Stones++;
                }

                return playerSnake;
            }

            static private void CalculateEnemySnakes(
                IReadOnlyDictionary<BoardElement, ImmutableHashSet<PointInfo>> boardElementToInfosIndex,
                IReadOnlyDictionary<Point, PointInfo> locationToPointInfoIndex,
                IReadOnlyList<ISnakeInfo> previousTickSnakeInfos,
                IReadOnlyDictionary<Point, PointInfo> previousTickLocationToPointInfoIndex,
                List<SnakeInfo> snakeInfos)
            {
                foreach (PointInfo enemyHead in GetEnemyHeads(boardElementToInfosIndex))
                {
                    var enemySnake = new SnakeInfo
                    {
                        FoundHead = true,
                        FuryTicksLeft = enemyHead.BoardElement == BoardElement.EnemyHeadEvil ? -1 : 0,
                        IsActive =
                            enemyHead.BoardElement != BoardElement.EnemyHeadSleep &&
                            enemyHead.BoardElement != BoardElement.EnemyHeadDead,
                        IsPlayerSnake = false
                    };

                    enemySnake.AddHead(enemyHead.Location);

                    bool foundNext = true;
                    while (foundNext)
                    {
                        foundNext = false;

                        PointInfo currentTail = locationToPointInfoIndex[enemySnake.Tail];
                        if (TryGetNextEnemyPoint(enemySnake, currentTail, out PointInfo newTail, searchForBody: true))
                        {
                            enemySnake.AddTail(newTail.Location);
                            foundNext = true;
                        }
                        else if (TryGetNextEnemyPoint(enemySnake, currentTail, out newTail, searchForTail: true))
                        {
                            enemySnake.AddTail(newTail.Location);
                            enemySnake.FoundTail = true;
                            // Если найдены хвост и голова, значит найдено всё тело.
                        }
                    }

                    snakeInfos.Add(enemySnake);
                }

                foreach (PointInfo enemyTail in GetEnemyTails(boardElementToInfosIndex))
                {
                    if (snakeInfos.Any(x => x.BodyPoints.Contains(enemyTail.Location)))
                    {
                        continue;
                    }

                    var enemySnake = new SnakeInfo
                    {
                        FoundTail = true,
                        FuryTicksLeft = -2,
                        IsActive = enemyTail.BoardElement != BoardElement.TailInactive,
                        IsPlayerSnake = false
                    };

                    enemySnake.AddTail(enemyTail.Location);

                    bool foundNext = true;
                    while (foundNext)
                    {
                        foundNext = false;

                        PointInfo currentHead = locationToPointInfoIndex[enemySnake.Head];
                        if (TryGetNextEnemyPoint(enemySnake, currentHead, out PointInfo newHead, searchForBody: true))
                        {
                            enemySnake.AddHead(newHead.Location);
                            foundNext = true;
                        }

                        // Здесь искать голову не надо, т.к. иначе было бы foundHead == true.
                    }

                    snakeInfos.Add(enemySnake);
                }

                foreach (PointInfo enemyBody in GetEnemyBodys(boardElementToInfosIndex))
                {
                    if (snakeInfos.Any(x => x.BodyPoints.Contains(enemyBody.Location)))
                    {
                        continue;
                    }

                    var enemySnake = new SnakeInfo
                    {
                        FoundHead = false,
                        FoundTail = false,
                        FuryTicksLeft = -2,
                        IsPlayerSnake = false,
                        // Неактивного тела не бывает.
                        IsActive = true
                    };

                    enemySnake.AddTail(enemyBody.Location);

                    bool foundNext = true;
                    while (foundNext)
                    {
                        foundNext = false;

                        PointInfo currentHead = locationToPointInfoIndex[enemySnake.Head];
                        bool gotNewHead = TryGetNextEnemyPoint(enemySnake, currentHead, out PointInfo newHead, searchForBody: true);
                        if (gotNewHead)
                        {
                            enemySnake.AddHead(newHead.Location);
                            foundNext = true;
                        }

                        PointInfo currentTail = locationToPointInfoIndex[enemySnake.Tail];
                        bool gotNewTail = TryGetNextEnemyPoint(enemySnake, currentTail, out PointInfo newTail, searchForBody: true);
                        if (gotNewTail)
                        {
                            enemySnake.AddTail(newTail.Location);
                            foundNext = true;
                        }
                    }

                    ISnakeInfo previousStateSnake = previousTickSnakeInfos?.FirstOrDefault(
                        x =>
                            !x.IsPlayerSnake &&
                            x.BodyPoints
                                .Take(x.Length - 1)
                                .All(point => enemySnake.BodyPoints.Contains(point))
                    );

                    if (previousStateSnake != null)
                    {
                        Point previousTail = previousStateSnake.Tail;

                        // Когда змейка уползает, её хвост сдвигается. Таким образом, если текущее тело содержит старый хвост в позиции 1, то это не хвост, а голова.
                        if (enemySnake.Neck == previousTail)
                        {
                            enemySnake.ReverseBody();
                        }
                    }

                    snakeInfos.Add(enemySnake);
                }

                foreach (SnakeInfo snakeInfo in snakeInfos)
                {
                    if (!snakeInfo.IsPlayerSnake && snakeInfo.FuryTicksLeft < 0)
                    {
                        // TODO check
                        ISnakeInfo previousStateSnake = previousTickSnakeInfos?.FirstOrDefault(
                            x =>
                                !x.IsPlayerSnake &&
                                x.BodyPoints
                                    .Take(x.Length - 1)
                                    .All(point => snakeInfo.BodyPoints.Contains(point))
                        );

                        if (previousStateSnake != null)
                        {
                            snakeInfo.FuryTicksLeft = Math.Max(previousStateSnake.FuryTicksLeft - 1, 0);
                        }
                        else
                        {
                            snakeInfo.FuryTicksLeft = Consts.FuryDuration;
                        }

                        if (previousTickLocationToPointInfoIndex?[snakeInfo.Head].BoardElement == BoardElement.FuryPill)
                        {
                            snakeInfo.FuryTicksLeft += Consts.FuryDuration;
                        }
                    }
                }
            }

            static private ImmutableHashSet<PointInfo> GetPlayerHeads(
                IReadOnlyDictionary<BoardElement, ImmutableHashSet<PointInfo>> boardElementToInfosIndex)
            {
                return GameBoardHelpers.SelectFirstNotEmptySet(
                    boardElementToInfosIndex,
                    BoardElement.HeadDown,
                    BoardElement.HeadLeft,
                    BoardElement.HeadRight,
                    BoardElement.HeadUp,
                    BoardElement.HeadDead,
                    BoardElement.HeadEvil,
                    BoardElement.HeadFly,
                    BoardElement.HeadSleep
                );
            }

            static private ImmutableHashSet<PointInfo> GetEnemyHeads(
                IReadOnlyDictionary<BoardElement, ImmutableHashSet<PointInfo>> boardElementToInfosIndex)
            {
                return GameBoardHelpers.JoinNotEmptySets(
                    boardElementToInfosIndex,
                    BoardElement.EnemyHeadDown,
                    BoardElement.EnemyHeadLeft,
                    BoardElement.EnemyHeadRight,
                    BoardElement.EnemyHeadUp,
                    BoardElement.EnemyHeadDead,
                    BoardElement.EnemyHeadEvil,
                    BoardElement.EnemyHeadFly,
                    BoardElement.EnemyHeadSleep
                )
                ?? new HashSet<PointInfo>().ToImmutableHashSet();
            }

            static private ImmutableHashSet<PointInfo> GetPlayerTails(
                IReadOnlyDictionary<BoardElement, ImmutableHashSet<PointInfo>> boardElementToInfosIndex)
            {
                return GameBoardHelpers.SelectFirstNotEmptySet(
                    boardElementToInfosIndex,
                    BoardElement.TailEndDown,
                    BoardElement.TailEndLeft,
                    BoardElement.TailEndUp,
                    BoardElement.TailEndRight,
                    BoardElement.TailInactive
                );
            }

            static private ImmutableHashSet<PointInfo> GetEnemyTails(
                IReadOnlyDictionary<BoardElement, ImmutableHashSet<PointInfo>> boardElementToInfosIndex)
            {
                return GameBoardHelpers.JoinNotEmptySets(
                    boardElementToInfosIndex,
                    BoardElement.EnemyTailEndDown,
                    BoardElement.EnemyTailEndLeft,
                    BoardElement.EnemyTailEndUp,
                    BoardElement.EnemyTailEndRight,
                    BoardElement.EnemyTailInactive
                )
                ?? new HashSet<PointInfo>().ToImmutableHashSet();
            }

            static private ImmutableHashSet<PointInfo> GetPlayerBodyPart(
                IReadOnlyDictionary<BoardElement, ImmutableHashSet<PointInfo>> boardElementToInfosIndex)
            {
                return GameBoardHelpers.SelectFirstNotEmptySet(
                    boardElementToInfosIndex,
                    BoardElement.BodyHorizontal,
                    BoardElement.BodyVertical,
                    BoardElement.BodyLeftDown,
                    BoardElement.BodyLeftUp,
                    BoardElement.BodyRightDown,
                    BoardElement.BodyRightUp
                );
            }

            static private ImmutableHashSet<PointInfo> GetEnemyBodys(
                IReadOnlyDictionary<BoardElement, ImmutableHashSet<PointInfo>> boardElementToInfosIndex)
            {
                return GameBoardHelpers.JoinNotEmptySets(
                    boardElementToInfosIndex,
                    BoardElement.EnemyBodyHorizontal,
                    BoardElement.EnemyBodyVertical,
                    BoardElement.EnemyBodyLeftDown,
                    BoardElement.EnemyBodyLeftUp,
                    BoardElement.EnemyBodyRightDown,
                    BoardElement.EnemyBodyRightUp
                )
                ?? new HashSet<PointInfo>().ToImmutableHashSet();
            }

            static private bool TryGetNextPlayerPoint(
                ISnakeInfo snake,
                PointInfo currentPoint,
                out PointInfo newTail,
                bool searchForHead = false,
                bool searchForBody = false,
                bool searchForTail = false)
            {
                bool currentPointIsHead =
                    currentPoint.BoardElement == BoardElement.HeadEvil ||
                    currentPoint.BoardElement == BoardElement.HeadUp ||
                    currentPoint.BoardElement == BoardElement.HeadDown ||
                    currentPoint.BoardElement == BoardElement.HeadLeft ||
                    currentPoint.BoardElement == BoardElement.HeadRight ||
                    currentPoint.BoardElement == BoardElement.HeadSleep ||
                    currentPoint.BoardElement == BoardElement.HeadDead;

                bool currentPointIsBody =
                    currentPoint.BoardElement == BoardElement.BodyLeftUp ||
                    currentPoint.BoardElement == BoardElement.BodyLeftDown ||
                    currentPoint.BoardElement == BoardElement.BodyRightUp ||
                    currentPoint.BoardElement == BoardElement.BodyRightDown ||
                    currentPoint.BoardElement == BoardElement.BodyVertical ||
                    currentPoint.BoardElement == BoardElement.BodyHorizontal;

                bool currentPointIsTail =
                    currentPoint.BoardElement == BoardElement.TailInactive ||
                    currentPoint.BoardElement == BoardElement.TailEndUp ||
                    currentPoint.BoardElement == BoardElement.TailEndDown ||
                    currentPoint.BoardElement == BoardElement.TailEndLeft ||
                    currentPoint.BoardElement == BoardElement.TailEndRight;

                GetPlayerJoinDirections(
                    currentPoint,
                    up: out bool searchUp,
                    down: out bool searchDown,
                    left: out bool searchLeft,
                    right: out bool searchRight,
                    searchForHead: currentPointIsHead,
                    searchForBody: currentPointIsBody,
                    searchForTail: currentPointIsTail
                );

                if (searchUp && currentPoint.TopNeighbour != null && !snake.BodyPoints.Contains(currentPoint.TopNeighbour.Location))
                {
                    GetPlayerJoinDirections(
                        currentPoint.TopNeighbour,
                        up: out _,
                        down: out bool down,
                        left: out _,
                        right: out _,
                        searchForHead: searchForHead,
                        searchForBody: searchForBody,
                        searchForTail: searchForTail
                    );

                    if (down)
                    {
                        newTail = currentPoint.TopNeighbour;
                        return true;
                    }
                }

                if (searchDown && currentPoint.BottomNeighbour != null && !snake.BodyPoints.Contains(currentPoint.BottomNeighbour.Location))
                {
                    GetPlayerJoinDirections(
                        currentPoint.BottomNeighbour,
                        up: out bool up,
                        down: out _,
                        left: out _,
                        right: out _,
                        searchForHead: searchForHead,
                        searchForBody: searchForBody,
                        searchForTail: searchForTail
                    );

                    if (up)
                    {
                        newTail = currentPoint.BottomNeighbour;
                        return true;
                    }
                }

                if (searchLeft && currentPoint.LeftNeighbour != null && !snake.BodyPoints.Contains(currentPoint.LeftNeighbour.Location))
                {
                    GetPlayerJoinDirections(
                        currentPoint.LeftNeighbour,
                        up: out _,
                        down: out _,
                        left: out _,
                        right: out bool right,
                        searchForHead: searchForHead,
                        searchForBody: searchForBody,
                        searchForTail: searchForTail
                    );

                    if (right)
                    {
                        newTail = currentPoint.LeftNeighbour;
                        return true;
                    }
                }

                if (searchRight && currentPoint.RightNeighbour != null && !snake.BodyPoints.Contains(currentPoint.RightNeighbour.Location))
                {
                    GetPlayerJoinDirections(
                        currentPoint.RightNeighbour,
                        up: out _,
                        down: out _,
                        left: out bool left,
                        right: out _,
                        searchForHead: searchForHead,
                        searchForBody: searchForBody,
                        searchForTail: searchForTail
                    );

                    if (left)
                    {
                        newTail = currentPoint.RightNeighbour;
                        return true;
                    }
                }

                newTail = null;
                return false;
            }

            static private void GetPlayerJoinDirections(
                PointInfo bodyPoint,
                out bool up,
                out bool down,
                out bool left,
                out bool right,
                bool searchForHead = false,
                bool searchForBody = false,
                bool searchForTail = false)
            {
                up =
                    (
                        searchForHead &&
                        (
                            bodyPoint.BoardElement == BoardElement.HeadEvil ||
                            bodyPoint.BoardElement == BoardElement.HeadDown ||
                            bodyPoint.BoardElement == BoardElement.HeadLeft ||
                            bodyPoint.BoardElement == BoardElement.HeadRight ||
                            bodyPoint.BoardElement == BoardElement.HeadSleep ||
                            bodyPoint.BoardElement == BoardElement.HeadDead
                        )
                    ) ||
                    (
                        searchForBody &&
                        (
                            bodyPoint.BoardElement == BoardElement.BodyLeftUp ||
                            bodyPoint.BoardElement == BoardElement.BodyRightUp ||
                            bodyPoint.BoardElement == BoardElement.BodyVertical
                        )
                    ) ||
                    (
                        searchForTail &&
                        (
                            bodyPoint.BoardElement == BoardElement.TailInactive ||
                            bodyPoint.BoardElement == BoardElement.TailEndDown ||
                            bodyPoint.BoardElement == BoardElement.TailEndLeft ||
                            bodyPoint.BoardElement == BoardElement.TailEndRight
                        )
                    );

                down =
                    (
                        searchForHead &&
                        (
                            bodyPoint.BoardElement == BoardElement.HeadEvil ||
                            bodyPoint.BoardElement == BoardElement.HeadUp ||
                            bodyPoint.BoardElement == BoardElement.HeadLeft ||
                            bodyPoint.BoardElement == BoardElement.HeadRight ||
                            bodyPoint.BoardElement == BoardElement.HeadSleep ||
                            bodyPoint.BoardElement == BoardElement.HeadDead
                        )
                    ) ||
                    (
                        searchForBody &&
                        (
                            bodyPoint.BoardElement == BoardElement.BodyLeftDown ||
                            bodyPoint.BoardElement == BoardElement.BodyRightDown ||
                            bodyPoint.BoardElement == BoardElement.BodyVertical
                        )
                    ) ||
                    (
                        searchForTail &&
                        (
                            bodyPoint.BoardElement == BoardElement.TailInactive ||
                            bodyPoint.BoardElement == BoardElement.TailEndUp ||
                            bodyPoint.BoardElement == BoardElement.TailEndLeft ||
                            bodyPoint.BoardElement == BoardElement.TailEndRight
                        )
                    );

                left =
                    (
                        searchForHead &&
                        (
                            bodyPoint.BoardElement == BoardElement.HeadEvil ||
                            bodyPoint.BoardElement == BoardElement.HeadUp ||
                            bodyPoint.BoardElement == BoardElement.HeadDown ||
                            bodyPoint.BoardElement == BoardElement.HeadRight ||
                            bodyPoint.BoardElement == BoardElement.HeadSleep ||
                            bodyPoint.BoardElement == BoardElement.HeadDead
                        )
                    ) ||
                    (
                        searchForBody &&
                        (
                            bodyPoint.BoardElement == BoardElement.BodyLeftDown ||
                            bodyPoint.BoardElement == BoardElement.BodyLeftUp ||
                            bodyPoint.BoardElement == BoardElement.BodyHorizontal
                        )
                    ) ||
                    (
                        searchForTail &&
                        (
                            bodyPoint.BoardElement == BoardElement.TailInactive ||
                            bodyPoint.BoardElement == BoardElement.TailEndDown ||
                            bodyPoint.BoardElement == BoardElement.TailEndUp ||
                            bodyPoint.BoardElement == BoardElement.TailEndRight
                        )
                    );

                right =
                    (
                        searchForHead &&
                        (
                            bodyPoint.BoardElement == BoardElement.HeadEvil ||
                            bodyPoint.BoardElement == BoardElement.HeadUp ||
                            bodyPoint.BoardElement == BoardElement.HeadDown ||
                            bodyPoint.BoardElement == BoardElement.HeadLeft ||
                            bodyPoint.BoardElement == BoardElement.HeadSleep ||
                            bodyPoint.BoardElement == BoardElement.HeadDead
                        )
                    ) ||
                    (
                        searchForBody &&
                        (
                            bodyPoint.BoardElement == BoardElement.BodyRightDown ||
                            bodyPoint.BoardElement == BoardElement.BodyRightUp ||
                            bodyPoint.BoardElement == BoardElement.BodyHorizontal
                        )
                    ) ||
                    (
                        searchForTail &&
                        (
                            bodyPoint.BoardElement == BoardElement.TailInactive ||
                            bodyPoint.BoardElement == BoardElement.TailEndDown ||
                            bodyPoint.BoardElement == BoardElement.TailEndLeft ||
                            bodyPoint.BoardElement == BoardElement.TailEndUp
                        )
                    );
            }

            static private bool TryGetNextEnemyPoint(
                ISnakeInfo snake,
                PointInfo currentPoint,
                out PointInfo newTail,
                bool searchForHead = false,
                bool searchForBody = false,
                bool searchForTail = false)
            {
                bool currentPointIsHead =
                    currentPoint.BoardElement == BoardElement.EnemyHeadEvil ||
                    currentPoint.BoardElement == BoardElement.EnemyHeadUp ||
                    currentPoint.BoardElement == BoardElement.EnemyHeadDown ||
                    currentPoint.BoardElement == BoardElement.EnemyHeadLeft ||
                    currentPoint.BoardElement == BoardElement.EnemyHeadRight ||
                    currentPoint.BoardElement == BoardElement.EnemyHeadSleep ||
                    currentPoint.BoardElement == BoardElement.EnemyHeadDead;

                bool currentPointIsBody =
                    currentPoint.BoardElement == BoardElement.EnemyBodyLeftUp ||
                    currentPoint.BoardElement == BoardElement.EnemyBodyLeftDown ||
                    currentPoint.BoardElement == BoardElement.EnemyBodyRightUp ||
                    currentPoint.BoardElement == BoardElement.EnemyBodyRightDown ||
                    currentPoint.BoardElement == BoardElement.EnemyBodyVertical ||
                    currentPoint.BoardElement == BoardElement.EnemyBodyHorizontal;

                bool currentPointIsTail =
                    currentPoint.BoardElement == BoardElement.EnemyTailInactive ||
                    currentPoint.BoardElement == BoardElement.EnemyTailEndUp ||
                    currentPoint.BoardElement == BoardElement.EnemyTailEndDown ||
                    currentPoint.BoardElement == BoardElement.EnemyTailEndLeft ||
                    currentPoint.BoardElement == BoardElement.EnemyTailEndRight;

                GetEnemyJoinDirections(
                    currentPoint,
                    up: out bool searchUp,
                    down: out bool searchDown,
                    left: out bool searchLeft,
                    right: out bool searchRight,
                    searchForHead: currentPointIsHead,
                    searchForBody: currentPointIsBody,
                    searchForTail: currentPointIsTail
                );

                if (searchUp && currentPoint.TopNeighbour != null && !snake.BodyPoints.Contains(currentPoint.TopNeighbour.Location))
                {
                    GetEnemyJoinDirections(
                        currentPoint.TopNeighbour,
                        up: out _,
                        down: out bool down,
                        left: out _,
                        right: out _,
                        searchForHead: searchForHead,
                        searchForBody: searchForBody,
                        searchForTail: searchForTail
                    );

                    if (down)
                    {
                        newTail = currentPoint.TopNeighbour;
                        return true;
                    }
                }

                if (searchDown && currentPoint.BottomNeighbour != null && !snake.BodyPoints.Contains(currentPoint.BottomNeighbour.Location))
                {
                    GetEnemyJoinDirections(
                        currentPoint.BottomNeighbour,
                        up: out bool up,
                        down: out _,
                        left: out _,
                        right: out _,
                        searchForHead: searchForHead,
                        searchForBody: searchForBody,
                        searchForTail: searchForTail
                    );

                    if (up)
                    {
                        newTail = currentPoint.BottomNeighbour;
                        return true;
                    }
                }

                if (searchLeft && currentPoint.LeftNeighbour != null && !snake.BodyPoints.Contains(currentPoint.LeftNeighbour.Location))
                {
                    GetEnemyJoinDirections(
                        currentPoint.LeftNeighbour,
                        up: out _,
                        down: out _,
                        left: out _,
                        right: out bool right,
                        searchForHead: searchForHead,
                        searchForBody: searchForBody,
                        searchForTail: searchForTail
                    );

                    if (right)
                    {
                        newTail = currentPoint.LeftNeighbour;
                        return true;
                    }
                }

                if (searchRight && currentPoint.RightNeighbour != null && !snake.BodyPoints.Contains(currentPoint.RightNeighbour.Location))
                {
                    GetEnemyJoinDirections(
                        currentPoint.RightNeighbour,
                        up: out _,
                        down: out _,
                        left: out bool left,
                        right: out _,
                        searchForHead: searchForHead,
                        searchForBody: searchForBody,
                        searchForTail: searchForTail
                    );

                    if (left)
                    {
                        newTail = currentPoint.RightNeighbour;
                        return true;
                    }
                }

                newTail = null;
                return false;
            }

            static private void GetEnemyJoinDirections(
                PointInfo bodyPoint,
                out bool up,
                out bool down,
                out bool left,
                out bool right,
                bool searchForHead = false,
                bool searchForBody = false,
                bool searchForTail = false)
            {
                up =
                    (
                        searchForHead &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyHeadEvil ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadLeft ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadRight ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadSleep ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadDead
                        )
                    ) ||
                    (
                        searchForBody &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyBodyLeftUp ||
                            bodyPoint.BoardElement == BoardElement.EnemyBodyRightUp ||
                            bodyPoint.BoardElement == BoardElement.EnemyBodyVertical
                        )
                    ) ||
                    (
                        searchForTail &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyTailInactive ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndLeft ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndRight
                        )
                    );

                down =
                    (
                        searchForHead &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyHeadEvil ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadUp ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadLeft ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadRight ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadSleep ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadDead
                        )
                    ) ||
                    (
                        searchForBody &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyBodyLeftDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyBodyRightDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyBodyVertical
                        )
                    ) ||
                    (
                        searchForTail &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyTailInactive ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndUp ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndLeft ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndRight
                        )
                    );

                left =
                    (
                        searchForHead &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyHeadEvil ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadUp ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadRight ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadSleep ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadDead
                        )
                    ) ||
                    (
                        searchForBody &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyBodyLeftDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyBodyLeftUp ||
                            bodyPoint.BoardElement == BoardElement.EnemyBodyHorizontal
                        )
                    ) ||
                    (
                        searchForTail &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyTailInactive ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndUp ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndRight
                        )
                    );

                right =
                    (
                        searchForHead &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyHeadEvil ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadUp ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadLeft ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadSleep ||
                            bodyPoint.BoardElement == BoardElement.EnemyHeadDead
                        )
                    ) ||
                    (
                        searchForBody &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyBodyRightDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyBodyRightUp ||
                            bodyPoint.BoardElement == BoardElement.EnemyBodyHorizontal
                        )
                    ) ||
                    (
                        searchForTail &&
                        (
                            bodyPoint.BoardElement == BoardElement.EnemyTailInactive ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndDown ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndLeft ||
                            bodyPoint.BoardElement == BoardElement.EnemyTailEndUp
                        )
                    );
            }
        }
    }
}
