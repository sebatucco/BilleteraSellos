#!/usr/bin/env bash
# Prueba de humo de punta a punta: login, permisos, sellos, canjes y avisos.
# Necesita la API corriendo en Development (con los datos de ejemplo recién cargados) y
# Seguridad__PedidosLoginPorMinuto alto, porque hace muchos logins seguidos.
#
#   bash pruebas/humo.sh http://localhost:5225
#
# Sale con código 1 si alguna verificación falla.
set -u
BASE=${1:-${URL_API:-http://localhost:5225}}
B="${BASE%/}/api"
J='Content-Type: application/json'
R=$(mktemp)
trap 'rm -f "$R"' EXIT
pass=0; fail=0
chk(){ # chk "descripcion" esperado obtenido
  if [ "$2" == "$3" ]; then pass=$((pass+1)); echo "OK   $1"; else fail=$((fail+1)); echo "FAIL $1 (esperado $2, obtenido $3)"; echo "     respuesta: $(head -c 300 "$R")"
    # En GitHub Actions, cada falla queda además como anotación visible en el resumen del job.
    [ -n "${GITHUB_ACTIONS:-}" ] && echo "::error title=Prueba de humo::$1 (esperado $2, obtenido $3) $(head -c 200 "$R" | tr -d '\n')"; fi; }
code(){ curl -s -o "$R" -w "%{http_code}" "$@"; }
body(){ cat "$R"; }
tok(){ jq -r '.token // empty' "$R"; }
campo(){ jq -r "$1" "$R"; }

echo "== Salud"
chk "salud responde" 200 $(code "${BASE%/}/salud")

echo "== Sin login"
chk "directorio público" 200 $(code $B/comercios)
chk "resumen sin token -> 401" 401 $(code $B/comercios/1/resumen)
chk "billetera sin token -> 401" 401 $(code $B/billetera)
chk "token inventado -> 401" 401 $(code -H "Authorization: Bearer xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" $B/comercios/1/resumen)

echo "== Login comercio"
chk "contraseña mala -> 401" 401 $(code -X POST -H "$J" -d '{"email":"cafe@demo.com","contrasena":"mala1234"}' $B/auth/comercios/login)
chk "email inexistente -> 401 mismo mensaje" 401 $(code -X POST -H "$J" -d '{"email":"nadie@demo.com","contrasena":"mala1234"}' $B/auth/comercios/login)
chk "dueño café OK" 200 $(code -X POST -H "$J" -d '{"email":"CAFE@demo.com ","contrasena":"Demo1234"}' $B/auth/comercios/login); DUENO=$(tok)
chk "cajero café OK" 200 $(code -X POST -H "$J" -d '{"email":"caja@cafe.demo","contrasena":"Demo1234"}' $B/auth/comercios/login); CAJA=$(tok); CAJA_ID=$(campo .usuarioId)
chk "dueño barbería OK" 200 $(code -X POST -H "$J" -d '{"email":"barberia@demo.com","contrasena":"Demo1234"}' $B/auth/comercios/login); BARB=$(tok)
chk "yo (dueño)" 200 $(code -H "Authorization: Bearer $DUENO" $B/auth/yo); echo "     $(body)"

echo "== Aislamiento entre comercios"
chk "café ve su resumen" 200 $(code -H "Authorization: Bearer $DUENO" $B/comercios/1/resumen)
chk "café NO ve resumen de barbería -> 403" 403 $(code -H "Authorization: Bearer $DUENO" $B/comercios/2/resumen)
chk "barbería NO ve clientes en riesgo del café -> 403" 403 $(code -H "Authorization: Bearer $BARB" $B/comercios/1/clientes-en-riesgo)
chk "barbería NO suma sellos en café -> 403" 403 $(code -X POST -H "$J" -H "Authorization: Bearer $BARB" -d '{"clienteId":1}' $B/comercios/1/sellos)

echo "== Roles"
chk "cajero suma sello" 200 $(code -X POST -H "$J" -H "Authorization: Bearer $CAJA" -d '{"clienteId":2}' $B/comercios/1/sellos)
chk "cajero ve programa" 200 $(code -H "Authorization: Bearer $CAJA" $B/comercios/1/programa)
chk "cajero NO cambia programa -> 403" 403 $(code -X PUT -H "$J" -H "Authorization: Bearer $CAJA" -d '{"sellosNecesarios":5,"premio":"x","costoPremio":1,"diasParaRetirar":7,"horasEntreSellos":0}' $B/comercios/1/programa)
chk "dueño cambia programa" 200 $(code -X PUT -H "$J" -H "Authorization: Bearer $DUENO" -d '{"sellosNecesarios":10,"premio":"Café con medialuna gratis","costoPremio":900,"diasParaRetirar":7,"horasEntreSellos":0}' $B/comercios/1/programa)
chk "cajero NO ve equipo -> 403" 403 $(code -H "Authorization: Bearer $CAJA" $B/comercios/1/usuarios)
chk "dueño ve equipo" 200 $(code -H "Authorization: Bearer $DUENO" $B/comercios/1/usuarios)
chk "dueño crea cajero con contraseña débil -> 400" 400 $(code -X POST -H "$J" -H "Authorization: Bearer $DUENO" -d '{"nombre":"Pepe","email":"pepe@cafe.demo","contrasena":"123"}' $B/comercios/1/usuarios)
chk "dueño crea cajero" 201 $(code -X POST -H "$J" -H "Authorization: Bearer $DUENO" -d '{"nombre":"Pepe","email":"pepe@cafe.demo","contrasena":"Pepe2026"}' $B/comercios/1/usuarios)
chk "barbería NO desactiva cajero del café -> 403" 403 $(code -X PUT -H "$J" -H "Authorization: Bearer $BARB" -d '{"activo":false}' $B/comercios/1/usuarios/$CAJA_ID/activo)
chk "dueño desactiva al cajero" 200 $(code -X PUT -H "$J" -H "Authorization: Bearer $DUENO" -d '{"activo":false}' $B/comercios/1/usuarios/$CAJA_ID/activo)
chk "token del cajero desactivado ya no sirve -> 401" 401 $(code -H "Authorization: Bearer $CAJA" $B/comercios/1/resumen)
chk "cajero desactivado no puede entrar -> 403" 403 $(code -X POST -H "$J" -d '{"email":"caja@cafe.demo","contrasena":"Demo1234"}' $B/auth/comercios/login)

echo "== Alta en la caja"
chk "alta de cliente en caja" 201 $(code -X POST -H "$J" -H "Authorization: Bearer $DUENO" -d '{"nombre":"Pedro Sosa","dni":"40.111.222","telefono":"11 7000-1111"}' $B/comercios/1/clientes)
chk "alta duplicada -> 409" 409 $(code -X POST -H "$J" -H "Authorization: Bearer $DUENO" -d '{"nombre":"Pedro Sosa","dni":"40111222","telefono":"1170001112"}' $B/comercios/1/clientes)

echo "== Cliente existente entra por SMS"
chk "pide código" 200 $(code -X POST -H "$J" -d '{"telefono":"+54 9 11 5487-3320"}' $B/auth/clientes/codigo); COD=$(campo .codigoDesarrollo); echo "     código=$COD"
chk "pedir otro enseguida -> 429" 429 $(code -X POST -H "$J" -d '{"telefono":"1154873320"}' $B/auth/clientes/codigo)
chk "código incorrecto -> 401" 401 $(code -X POST -H "$J" -d '{"telefono":"1154873320","codigo":"000000"}' $B/auth/clientes/verificar)
chk "código correcto" 200 $(code -X POST -H "$J" -d "{\"telefono\":\"1154873320\",\"codigo\":\"$COD\"}" $B/auth/clientes/verificar); LUCIA=$(tok); echo "     $(body | cut -c1-120)"
chk "mismo código no sirve dos veces -> 401" 401 $(code -X POST -H "$J" -d "{\"telefono\":\"1154873320\",\"codigo\":\"$COD\"}" $B/auth/clientes/verificar)
chk "billetera de Lucía" 200 $(code -H "Authorization: Bearer $LUCIA" $B/billetera); echo "     $(body | cut -c1-90)"
chk "cliente NO entra al panel -> 403" 403 $(code -H "Authorization: Bearer $LUCIA" $B/comercios/1/resumen)
chk "comercio NO entra a una billetera -> 403" 403 $(code -H "Authorization: Bearer $DUENO" $B/billetera)
chk "suma tarjeta por QR" 201 $(code -X POST -H "$J" -H "Authorization: Bearer $LUCIA" -d '{"codigoComercio":"dona-rosa"}' $B/billetera/tarjetas)

echo "== Cliente nuevo: código + registro"
chk "pide código" 200 $(code -X POST -H "$J" -d '{"telefono":"1199990000"}' $B/auth/clientes/codigo); COD2=$(campo .codigoDesarrollo)
chk "verifica: requiere registro" 200 $(code -X POST -H "$J" -d "{\"telefono\":\"1199990000\",\"codigo\":\"$COD2\"}" $B/auth/clientes/verificar); REG=$(tok); echo "     $(body | cut -c1-60)"
chk "token de registro NO abre billetera -> 403" 403 $(code -H "Authorization: Bearer $REG" $B/billetera)
chk "registro sin token -> 401" 401 $(code -X POST -H "$J" -d '{"nombre":"Ana","dni":"35123456"}' $B/clientes)
chk "registro con token" 201 $(code -X POST -H "$J" -H "Authorization: Bearer $REG" -d '{"nombre":"Ana Torres","dni":"35123456","fechaNacimiento":"1990-05-02"}' $B/clientes); ANA=$(tok)
chk "token de registro ya no sirve -> 401" 401 $(code -X POST -H "$J" -H "Authorization: Bearer $REG" -d '{"nombre":"Otra","dni":"35123457"}' $B/clientes)
chk "billetera de Ana" 200 $(code -H "Authorization: Bearer $ANA" $B/billetera)

echo "== Cliente dado de alta en caja entra después con SMS"
code -X POST -H "$J" -d '{"telefono":"1170001111"}' $B/auth/clientes/codigo >/dev/null; COD3=$(campo .codigoDesarrollo)
chk "Pedro entra directo (sin registro)" 200 $(code -X POST -H "$J" -d "{\"telefono\":\"1170001111\",\"codigo\":\"$COD3\"}" $B/auth/clientes/verificar); echo "     $(body | grep -o '"requiereRegistro":[a-z]*')"

echo "== Canje completo (barbería)"
chk "la barbería saca la espera entre sellos" 200 $(code -X PUT -H "$J" -H "Authorization: Bearer $BARB" -d '{"sellosNecesarios":6,"premio":"Corte gratis","costoPremio":6000,"diasParaRetirar":7,"horasEntreSellos":0}' $B/comercios/2/programa)
chk "dueño barbería suma el sello que completa a Lucía" 200 $(code -X POST -H "$J" -H "Authorization: Bearer $BARB" -d '{"clienteId":1}' $B/comercios/2/sellos); chk "tarjeta completa" true $(campo .tarjetaCompleta)
chk "con la tarjeta completa suma igual (el sobrante se guarda)" 200 $(code -X POST -H "$J" -H "Authorization: Bearer $BARB" -d '{"clienteId":1}' $B/comercios/2/sellos); chk "quedan 7 sellos de 6" 7 $(campo .sellosActuales)
code -H "Authorization: Bearer $LUCIA" $B/billetera >/dev/null; TB=$(campo '.tarjetas[] | select(.comercio=="Barbería Norte") | .tarjetaId')
chk "Lucía canjea" 200 $(code -X POST -H "Authorization: Bearer $LUCIA" $B/billetera/tarjetas/$TB/canjear); CANJE=$(campo .codigo)
code -H "Authorization: Bearer $LUCIA" $B/billetera >/dev/null
chk "después del canje le queda 1 sello de sobrante" 1 $(campo '.tarjetas[] | select(.comercio=="Barbería Norte") | .sellosActuales')
chk "Ana no puede canjear la tarjeta de Lucía -> 404" 404 $(code -X POST -H "Authorization: Bearer $ANA" $B/billetera/tarjetas/$TB/canjear)
chk "el café no puede entregar un canje de la barbería -> 404" 404 $(code -X POST -H "Authorization: Bearer $DUENO" $B/comercios/1/canjes/$CANJE/entregar)
chk "la barbería entrega el premio" 200 $(code -X POST -H "Authorization: Bearer $BARB" $B/comercios/2/canjes/$CANJE/entregar)
chk "no se entrega dos veces -> 409" 409 $(code -X POST -H "Authorization: Bearer $BARB" $B/comercios/2/canjes/$CANJE/entregar)

echo "== QR de la compra (Supermercado Gómez: 1 sello cada \$10.000, compra mínima \$5.000)"
chk "dueño del súper entra" 200 $(code -X POST -H "$J" -d '{"email":"super@demo.com","contrasena":"Demo1234"}' $B/auth/comercios/login); SUPER=$(tok); SID=$(campo .comercioId)
chk "cajera del súper entra" 200 $(code -X POST -H "$J" -d '{"email":"caja1@super.demo","contrasena":"Demo1234"}' $B/auth/comercios/login); CAJA1=$(tok)
# Los decimales pueden venir como 10000 o 10000.00 según la base: se comparan como número.
chk "el programa del súper tiene monto por sello" true $(code -H "Authorization: Bearer $SUPER" $B/comercios/$SID/programa >/dev/null; campo '.montoPorSello == 10000')
chk "QR sin monto -> 400" 400 $(code -X POST -H "$J" -H "Authorization: Bearer $CAJA1" -d '{}' $B/comercios/$SID/compras)
chk "QR debajo de la compra mínima -> 400" 400 $(code -X POST -H "$J" -H "Authorization: Bearer $CAJA1" -d '{"monto":3000}' $B/comercios/$SID/compras)
chk "QR con un monto absurdo -> 400" 400 $(code -X POST -H "$J" -H "Authorization: Bearer $CAJA1" -d '{"monto":35000000}' $B/comercios/$SID/compras)
chk "la cajera genera el QR de \$35.000" 201 $(code -X POST -H "$J" -H "Authorization: Bearer $CAJA1" -d '{"monto":35000}' $B/comercios/$SID/compras); QR=$(campo .codigo); chk "da 3 sellos" 3 $(campo .sellos)
chk "el QR está pendiente" Pendiente $(code -H "Authorization: Bearer $CAJA1" $B/comercios/$SID/compras/$QR >/dev/null; campo .estado)
chk "el café NO ve el QR del súper -> 403" 403 $(code -H "Authorization: Bearer $DUENO" $B/comercios/$SID/compras/$QR)
chk "por la ruta del café el QR del súper no existe -> 404" 404 $(code -H "Authorization: Bearer $DUENO" $B/comercios/1/compras/$QR)
chk "un comercio NO puede usar un QR como cliente -> 403" 403 $(code -X POST -H "Authorization: Bearer $SUPER" $B/billetera/compras/$QR)
chk "Lucía escanea el QR" 200 $(code -X POST -H "Authorization: Bearer $LUCIA" $B/billetera/compras/$QR); chk "suma 3 sellos" 3 $(campo .sellosSumados); chk "tarjeta nueva del súper" true $(campo .tarjetaNueva)
chk "la caja ve que se usó" Usada $(code -H "Authorization: Bearer $CAJA1" $B/comercios/$SID/compras/$QR >/dev/null; campo .estado); chk "y quién lo usó" "Lucía F." "$(campo .cliente)"
chk "el mismo QR no sirve dos veces -> 409" 409 $(code -X POST -H "Authorization: Bearer $ANA" $B/billetera/compras/$QR)
chk "un QR usado no se puede anular -> 409" 409 $(code -X POST -H "Authorization: Bearer $CAJA1" $B/comercios/$SID/compras/$QR/anular)
chk "un QR inventado no existe -> 404" 404 $(code -X POST -H "Authorization: Bearer $LUCIA" $B/billetera/compras/qr-inventado-que-no-existe-0000000000)
chk "QR de \$12.000" 201 $(code -X POST -H "$J" -H "Authorization: Bearer $CAJA1" -d '{"monto":12000}' $B/comercios/$SID/compras); QR2=$(campo .codigo); chk "da 1 sello" 1 $(campo .sellos)
chk "la cajera lo anula" 204 $(code -X POST -H "Authorization: Bearer $CAJA1" $B/comercios/$SID/compras/$QR2/anular)
chk "queda anulado" Anulada $(code -H "Authorization: Bearer $CAJA1" $B/comercios/$SID/compras/$QR2 >/dev/null; campo .estado)
chk "un QR anulado no se puede usar -> 409" 409 $(code -X POST -H "Authorization: Bearer $LUCIA" $B/billetera/compras/$QR2)
chk "por DNI sin monto en el súper -> 400" 400 $(code -X POST -H "$J" -H "Authorization: Bearer $CAJA1" -d '{"clienteId":1}' $B/comercios/$SID/sellos)
chk "por DNI con \$25.000" 200 $(code -X POST -H "$J" -H "Authorization: Bearer $CAJA1" -d '{"clienteId":1,"monto":25000}' $B/comercios/$SID/sellos); chk "suma 2 sellos" 2 $(campo .sellosSumados); chk "Lucía tiene 5 en el súper" 5 $(campo .sellosActuales)
chk "el resumen del súper cuenta 5 sellos del mes" 5 $(code -H "Authorization: Bearer $SUPER" $B/comercios/$SID/resumen >/dev/null; campo .sellosUltimos30Dias)

echo "== Panel"
chk "clientes en riesgo" 200 $(code -H "Authorization: Bearer $DUENO" $B/comercios/1/clientes-en-riesgo)
chk "cumpleaños" 200 $(code -H "Authorization: Bearer $DUENO" "$B/comercios/1/cumpleanos?dias=7")
chk "costo del premio" 200 $(code -H "Authorization: Bearer $DUENO" "$B/comercios/1/costo-premio?ticketPromedio=4500&comprasConSelloPorMes=600"); chk "costo mensual calculado" 54000 $(campo .costoMensual)
chk "avisos automáticos" 200 $(code -X POST $B/avisos/procesar)

echo "== Intentos y bloqueo"
code -X POST -H "$J" -d '{"telefono":"1188887777"}' $B/auth/clientes/codigo >/dev/null
for i in 1 2 3 4; do code -X POST -H "$J" -d '{"telefono":"1188887777","codigo":"111111"}' $B/auth/clientes/verificar >/dev/null; done
chk "5to intento fallido -> 429" 429 $(code -X POST -H "$J" -d '{"telefono":"1188887777","codigo":"111111"}' $B/auth/clientes/verificar)
for i in 1 2 3 4 5; do code -X POST -H "$J" -d '{"email":"barberia@demo.com","contrasena":"mala1234"}' $B/auth/comercios/login >/dev/null; done
chk "barbería bloqueada tras 5 fallos (aun con la buena) -> 429" 429 $(code -X POST -H "$J" -d '{"email":"barberia@demo.com","contrasena":"Demo1234"}' $B/auth/comercios/login); echo "     $(body)"

echo "== Contraseñas (verdulería)"
chk "verdulería sesión A" 200 $(code -X POST -H "$J" -d '{"email":"verduleria@demo.com","contrasena":"Demo1234"}' $B/auth/comercios/login); VA=$(tok)
chk "verdulería sesión B" 200 $(code -X POST -H "$J" -d '{"email":"verduleria@demo.com","contrasena":"Demo1234"}' $B/auth/comercios/login); VB=$(tok)
chk "cambiar con la actual mala -> 400" 400 $(code -X POST -H "$J" -H "Authorization: Bearer $VA" -d '{"actual":"mala1234","nueva":"Verdu2026"}' $B/auth/contrasena)
chk "cambiar sin login -> 401" 401 $(code -X POST -H "$J" -d '{"actual":"Demo1234","nueva":"Verdu2026"}' $B/auth/contrasena)
chk "cambiar bien" 200 $(code -X POST -H "$J" -H "Authorization: Bearer $VA" -d '{"actual":"Demo1234","nueva":"Verdu2026"}' $B/auth/contrasena)
chk "la otra sesión quedó cerrada -> 401" 401 $(code -H "Authorization: Bearer $VB" $B/comercios/4/resumen)
chk "la sesión que hizo el cambio sigue" 200 $(code -H "Authorization: Bearer $VA" $B/comercios/4/resumen)
chk "login con la vieja -> 401" 401 $(code -X POST -H "$J" -d '{"email":"verduleria@demo.com","contrasena":"Demo1234"}' $B/auth/comercios/login)
chk "login con la nueva" 200 $(code -X POST -H "$J" -d '{"email":"verduleria@demo.com","contrasena":"Verdu2026"}' $B/auth/comercios/login)
chk "olvidé (email existente)" 200 $(code -X POST -H "$J" -d '{"email":"verduleria@demo.com"}' $B/auth/comercios/olvide); MSG1=$(campo .mensaje)
chk "olvidé (email inexistente)" 200 $(code -X POST -H "$J" -d '{"email":"nadie@demo.com"}' $B/auth/comercios/olvide); MSG2=$(campo .mensaje)
chk "olvidé responde igual exista o no" si $([ -n "$MSG1" ] && [ "$MSG1" == "$MSG2" ] && echo si || echo no)
chk "restablecer con token inventado -> 400" 400 $(code -X POST -H "$J" -d '{"token":"tokeninventadotokeninventado123","nueva":"Otra2026"}' $B/auth/comercios/restablecer)

echo "== Registro de comercio y salir"
chk "registro comercio" 201 $(code -X POST -H "$J" -d '{"nombreComercio":"Café Tostado","rubro":"Cafetería","direccion":"Mitre 100","nombreDueno":"Leo","email":"leo@nuevo.com","contrasena":"Leo12345"}' $B/auth/comercios/registro); NUEVO=$(tok); NID=$(campo .comercioId)
chk "código único del QR" "cafe-tostado-2" $(curl -s $B/comercios | jq -r '.[] | select(.codigo=="cafe-tostado-2") | .codigo')
chk "comercio nuevo ve su resumen" 200 $(code -H "Authorization: Bearer $NUEVO" $B/comercios/$NID/resumen)
chk "email repetido -> 409" 409 $(code -X POST -H "$J" -d '{"nombreComercio":"X","rubro":"","direccion":"","nombreDueno":"X","email":"leo@nuevo.com","contrasena":"Leo12345"}' $B/auth/comercios/registro)
chk "salir" 204 $(code -X POST -H "Authorization: Bearer $NUEVO" $B/auth/salir)
chk "token tras salir -> 401" 401 $(code -H "Authorization: Bearer $NUEVO" $B/comercios/$NID/resumen)

echo; echo "Resultado: $pass OK, $fail FALLAS"
[ "$fail" -eq 0 ]
