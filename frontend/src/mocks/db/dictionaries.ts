export const MALE_NAMES = ['Алексей', 'Дмитрий', 'Сергей', 'Андрей', 'Максим', 'Иван', 'Николай', 'Павел', 'Артём', 'Михаил', 'Егор', 'Роман']
export const FEMALE_NAMES = ['Анна', 'Мария', 'Елена', 'Ольга', 'Ирина', 'Наталья', 'Екатерина', 'Татьяна', 'Светлана', 'Юлия', 'Дарья', 'Ксения']
export const SURNAMES = ['Иванов', 'Смирнов', 'Кузнецов', 'Попов', 'Васильев', 'Петров', 'Соколов', 'Михайлов', 'Новиков', 'Фёдоров', 'Морозов', 'Волков', 'Алексеев', 'Лебедев', 'Семёнов', 'Егоров', 'Павлов', 'Козлов', 'Степанов', 'Николаев']

export const DELIVERY_CITIES = ['Москва', 'Санкт-Петербург', 'Казань', 'Екатеринбург', 'Новосибирск', 'Краснодар', 'Нижний Новгород', 'Самара', 'Ростов-на-Дону', 'Уфа', 'Воронеж', 'Пермь', 'Челябинск', 'Тюмень']

export const VEHICLE_LETTERS = ['А', 'В', 'Е', 'К', 'М', 'Н', 'О', 'Р', 'С', 'Т', 'У', 'Х']

export interface FulfillmentSeed {
  name: string
  inn: string
  city: string
  warehouses: { name: string; code: string; city: string; address: string; areaSqm: number; aisles: number; racks: number }[]
}

export const FULFILLMENTS: FulfillmentSeed[] = [
  {
    name: 'СкладПро Фулфилмент',
    inn: '7714382910',
    city: 'Москва',
    warehouses: [
      { name: 'Подольск-1', code: 'PDL1', city: 'Подольск', address: 'Окружная ул., 4, стр. 2', areaSqm: 12000, aisles: 8, racks: 12 },
      { name: 'Химки', code: 'KHM1', city: 'Химки', address: 'Ленинградское ш., 29', areaSqm: 7500, aisles: 6, racks: 10 },
      { name: 'Казань', code: 'KZN1', city: 'Казань', address: 'ул. Тэцевская, 35', areaSqm: 5000, aisles: 5, racks: 9 },
    ],
  },
  {
    name: 'Логистик Плюс',
    inn: '7811650274',
    city: 'Санкт-Петербург',
    warehouses: [
      { name: 'Шушары', code: 'SHU1', city: 'Санкт-Петербург', address: 'Московское ш., 231', areaSqm: 9000, aisles: 5, racks: 8 },
      { name: 'Парнас', code: 'PRN1', city: 'Санкт-Петербург', address: '6-й Верхний пер., 12', areaSqm: 4200, aisles: 4, racks: 8 },
    ],
  },
  {
    name: 'ФФ Юг',
    inn: '2311298456',
    city: 'Краснодар',
    warehouses: [
      { name: 'Краснодар', code: 'KRD1', city: 'Краснодар', address: 'ул. Ростовское ш., 14/5', areaSqm: 6000, aisles: 5, racks: 8 },
    ],
  },
]

export interface SellerSeed {
  name: string
  inn: string
  city: string
  prefix: string
  category: string
  /** Средний объём заказов в день */
  dailyOrders: number
  priceRange: [number, number]
  weightRange: [number, number]
  tracksExpiry: boolean
  requiresMarking: boolean
  bases: string[]
  variants: string[]
  colors?: string[]
  /** Индексы ФЦ и складов, с которыми есть договор */
  contracts: { fulfillment: number; warehouses: number[] }[]
}

export const SELLERS: SellerSeed[] = [
  {
    name: 'Уютный дом',
    inn: '503214567890',
    city: 'Москва',
    prefix: 'UD',
    category: 'Домашний текстиль',
    dailyOrders: 10,
    priceRange: [690, 5490],
    weightRange: [300, 2500],
    tracksExpiry: false,
    requiresMarking: true,
    bases: ['Плед флисовый', 'Комплект постельного белья', 'Подушка ортопедическая', 'Полотенце махровое', 'Покрывало стёганое', 'Шторы блэкаут', 'Скатерть хлопковая', 'Наволочки сатин'],
    variants: ['150×200', '200×220', '50×70', '70×140', '1,5-спальный', 'евро'],
    colors: ['бежевый', 'серый', 'графит', 'мятный', 'пудровый', 'белый'],
    contracts: [
      { fulfillment: 0, warehouses: [0, 1] },
      { fulfillment: 1, warehouses: [0] },
    ],
  },
  {
    name: 'Кидс Ворлд',
    inn: '7728456123',
    city: 'Москва',
    prefix: 'KW',
    category: 'Детские товары',
    dailyOrders: 7,
    priceRange: [390, 4990],
    weightRange: [150, 1800],
    tracksExpiry: false,
    requiresMarking: false,
    bases: ['Конструктор магнитный', 'Пазл деревянный', 'Мягкая игрушка Зайка', 'Набор для рисования', 'Кубики развивающие', 'Машинка на радиоуправлении', 'Кукла с аксессуарами', 'Сортер'],
    variants: ['24 детали', '60 деталей', '30 см', '45 см', 'набор 12 шт'],
    colors: ['розовый', 'голубой', 'мульти'],
    contracts: [{ fulfillment: 0, warehouses: [0, 2] }],
  },
  {
    name: 'ТехноМир',
    inn: '7805123987',
    city: 'Санкт-Петербург',
    prefix: 'TM',
    category: 'Аксессуары для электроники',
    dailyOrders: 6,
    priceRange: [290, 3990],
    weightRange: [40, 600],
    tracksExpiry: false,
    requiresMarking: false,
    bases: ['Кабель USB-C', 'Зарядное устройство 65 Вт', 'Чехол для iPhone 15', 'Защитное стекло', 'Наушники TWS', 'Powerbank 20000 мАч', 'Держатель автомобильный', 'Умная лампа'],
    variants: ['1 м', '2 м', 'Pro', 'Lite'],
    colors: ['чёрный', 'белый', 'синий'],
    contracts: [
      { fulfillment: 1, warehouses: [0, 1] },
      { fulfillment: 0, warehouses: [1] },
    ],
  },
  {
    name: 'Спортлайф',
    inn: '231245678901',
    city: 'Краснодар',
    prefix: 'SL',
    category: 'Спорт и фитнес',
    dailyOrders: 8,
    priceRange: [350, 6990],
    weightRange: [200, 5200],
    tracksExpiry: false,
    requiresMarking: false,
    bases: ['Коврик для йоги', 'Гантели неопреновые', 'Эспандер ленточный', 'Бутылка для воды', 'Скакалка скоростная', 'Ролик массажный', 'Фитнес-резинки'],
    variants: ['2 кг', '5 кг', '750 мл', '6 мм', 'набор 5 шт'],
    colors: ['лиловый', 'чёрный', 'бирюзовый'],
    contracts: [
      { fulfillment: 2, warehouses: [0] },
      { fulfillment: 0, warehouses: [0] },
    ],
  },
  {
    name: 'Бьюти Лаб',
    inn: '7701987654',
    city: 'Москва',
    prefix: 'BL',
    category: 'Косметика',
    dailyOrders: 5,
    priceRange: [290, 2490],
    weightRange: [60, 500],
    tracksExpiry: true,
    requiresMarking: true,
    bases: ['Сыворотка с витамином C', 'Крем для лица увлажняющий', 'Маска тканевая', 'Шампунь бессульфатный', 'Бальзам для губ', 'Мицеллярная вода', 'Патчи для глаз'],
    variants: ['30 мл', '50 мл', '250 мл', '400 мл', '60 шт'],
    contracts: [{ fulfillment: 0, warehouses: [0, 1] }],
  },
  {
    name: 'ЗооМаркет',
    inn: '1655432109',
    city: 'Казань',
    prefix: 'ZM',
    category: 'Зоотовары',
    dailyOrders: 4,
    priceRange: [190, 3490],
    weightRange: [100, 10500],
    tracksExpiry: true,
    requiresMarking: false,
    bases: ['Корм сухой для кошек', 'Корм для собак мелких пород', 'Наполнитель древесный', 'Лакомство для собак', 'Когтеточка', 'Шлейка нейлоновая'],
    variants: ['1,5 кг', '3 кг', '10 л', '200 г', 'размер S', 'размер M'],
    contracts: [{ fulfillment: 0, warehouses: [2] }],
  },
  {
    name: 'Чайная лавка',
    inn: '781609876543',
    city: 'Санкт-Петербург',
    prefix: 'CL',
    category: 'Чай и продукты',
    dailyOrders: 3,
    priceRange: [250, 2990],
    weightRange: [100, 900],
    tracksExpiry: true,
    requiresMarking: false,
    bases: ['Чай улун Те Гуань Инь', 'Чай пуэр шу', 'Чай зелёный Сенча', 'Иван-чай ферментированный', 'Набор чайный подарочный', 'Мёд гречишный'],
    variants: ['100 г', '250 г', '500 г', 'набор 4 шт'],
    contracts: [{ fulfillment: 1, warehouses: [1] }],
  },
  {
    name: 'Стиль',
    inn: '7709345612',
    city: 'Москва',
    prefix: 'ST',
    category: 'Одежда',
    dailyOrders: 6,
    priceRange: [990, 7990],
    weightRange: [150, 1200],
    tracksExpiry: false,
    requiresMarking: true,
    bases: ['Футболка оверсайз', 'Худи с капюшоном', 'Джинсы прямые', 'Платье трикотажное', 'Свитшот базовый', 'Брюки карго'],
    variants: ['XS', 'S', 'M', 'L', 'XL'],
    colors: ['чёрный', 'белый', 'молочный', 'хаки', 'синий'],
    contracts: [
      { fulfillment: 0, warehouses: [0] },
      { fulfillment: 2, warehouses: [0] },
    ],
  },
]

export const PENDING_ORGS = [
  { type: 'fulfillment' as const, name: 'Сибирь Фулфилмент', inn: '5406789012', city: 'Новосибирск', owner: 'Павел Егоров' },
  { type: 'seller' as const, name: 'ЭкоБьюти', inn: '7743210987', city: 'Москва', owner: 'Ксения Лебедева' },
  { type: 'seller' as const, name: 'Мастер Инструмент', inn: '6658123456', city: 'Екатеринбург', owner: 'Роман Козлов' },
]
