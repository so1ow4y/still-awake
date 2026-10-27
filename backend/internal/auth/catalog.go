package auth

// Модель доступа — как в license-store: право <домен>.<действие> закрывает конкретные маршруты API,
// роль — набор прав с уровнем (priority). Системные роли описаны здесь и пересобираются при каждом
// запуске сервера, из панели они только читаются (их можно взять за основу своей роли).
//
// Правило иерархии одно на всё: менять можно только пользователей, чья старшая роль ниже твоей,
// назначать и создавать — только роли ниже своей, а в свою роль включать — только права, которые есть у тебя.

type PermissionDef struct {
	Slug        string
	Group       string
	Description string
}

type RoleDef struct {
	Slug        string
	Name        string
	Description string
	Priority    int
	// nil — все права системы (супер-админ).
	Permissions []string
}

const (
	PermUsersRead      = "users.read"
	PermUsersCreate    = "users.create"
	PermUsersUpdate    = "users.update"
	PermUsersBlock     = "users.block"
	PermRolesRead      = "roles.read"
	PermRolesAssign    = "roles.assign"
	PermRolesManage    = "roles.manage"
	PermLoginsRead     = "logins.read"
	PermLLMSettings    = "llm.settings"
	PermLLMPrompt      = "llm.prompt"
	PermLLMDebug       = "llm.debug"
	PermAnalyticsRead  = "analytics.read"
	PermSessionsRead   = "sessions.read"
	PermSessionsManage = "sessions.manage"
	PermDBRead         = "db.read"
	PermDBWrite        = "db.write"
	PermGamePlay       = "game.play"
)

var Permissions = []PermissionDef{
	{PermUsersRead, "users", "Просмотр сотрудников и игроков"},
	{PermUsersCreate, "users", "Создание сотрудников: логин и пароль"},
	{PermUsersUpdate, "users", "Изменение логина, имени и пароля пользователей"},
	{PermUsersBlock, "users", "Блокировка, разблокировка и завершение сессий"},
	{PermRolesRead, "users", "Просмотр ролей"},
	{PermRolesAssign, "users", "Назначение ролей (только ниже своей)"},
	{PermRolesManage, "users", "Свои роли из набора прав (только ниже своей)"},
	{PermLoginsRead, "security", "Журнал входов"},
	{PermLLMSettings, "llm", "Провайдер модели: адрес, модель, проверка"},
	{PermLLMPrompt, "llm", "Системный промпт: правка и версии"},
	{PermLLMDebug, "llm", "Отладочный чат с моделью и подмена провайдера в запросе"},
	{PermAnalyticsRead, "data", "Аналитика: запросы, токены, задержки"},
	{PermSessionsRead, "data", "Игровые сессии, сообщения, события и запросы к модели"},
	{PermSessionsManage, "data", "Правка и удаление игровых данных"},
	{PermDBRead, "data", "SQL-консоль: схема и запросы на чтение"},
	{PermDBWrite, "data", "SQL-консоль: изменяющие запросы"},
	{PermGamePlay, "game", "Игра: аккаунт по телефону, сессии и чат"},
}

const (
	RoleSuperAdmin = "super_admin"
	RolePlayer     = "player"
)

var SystemRoles = []RoleDef{
	{
		Slug: RoleSuperAdmin, Name: "Супер-админ", Priority: 100,
		Description: "Все возможности, включая назначение любых ролей и изменяющий SQL.",
	},
	{
		Slug: "admin", Name: "Администратор", Priority: 80,
		Description: "Сотрудники и игроки, роли ниже своей, модель и промпт, игровые данные. SQL — только чтение.",
		Permissions: []string{
			PermUsersRead, PermUsersCreate, PermUsersUpdate, PermUsersBlock,
			PermRolesRead, PermRolesAssign, PermRolesManage, PermLoginsRead,
			PermLLMSettings, PermLLMPrompt, PermLLMDebug,
			PermAnalyticsRead, PermSessionsRead, PermSessionsManage, PermDBRead,
		},
	},
	{
		Slug: "llm_engineer", Name: "Инженер модели", Priority: 60,
		Description: "Провайдер и модель, системный промпт, отладочный чат; просмотр сессий и аналитики.",
		Permissions: []string{PermLLMSettings, PermLLMPrompt, PermLLMDebug, PermSessionsRead, PermAnalyticsRead},
	},
	{
		Slug: "support", Name: "Поддержка", Priority: 60,
		Description: "Игроки и сотрудники: просмотр, блокировка, журнал входов, игровые сессии.",
		Permissions: []string{PermUsersRead, PermUsersBlock, PermRolesRead, PermLoginsRead, PermSessionsRead},
	},
	{
		Slug: "analyst", Name: "Аналитик", Priority: 50,
		Description: "Аналитика, игровые сессии и запросы к модели — только просмотр; SQL на чтение.",
		Permissions: []string{PermAnalyticsRead, PermSessionsRead, PermDBRead},
	},
	{
		Slug: RolePlayer, Name: "Игрок", Priority: 10,
		Description: "Аккаунт из игры: регистрация по номеру телефона, игровые сессии и чат.",
		Permissions: []string{PermGamePlay},
	},
}

// Права уровня игрока: роль только из них может иметь самый низкий уровень.
var PlayerLevel = map[string]bool{PermGamePlay: true}

// Уровни своих ролей: от CustomRoleMin до CustomRoleMax и всегда ниже уровня создателя.
const (
	CustomRoleMin = 11
	CustomRoleMax = 99
)
